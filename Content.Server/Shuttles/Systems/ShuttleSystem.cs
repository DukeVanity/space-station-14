using Content.Server.Doors.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Stunnable;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Systems;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server.Shuttles.Systems;

[UsedImplicitly]
public sealed partial class ShuttleSystem : SharedShuttleSystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AirlockSystem _airlock = default!;
    [Dependency] private readonly DockingSystem _dockSystem = default!;
    [Dependency] private readonly DoorSystem _doors = default!;
    [Dependency] private readonly FixtureSystem _fixtures = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ShuttleConsoleSystem _console = default!;
    [Dependency] private readonly StunSystem _stuns = default!;
    [Dependency] private readonly ThrusterSystem _thruster = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;

    private ISawmill _sawmill = default!;

    public const float TileMassMultiplier = 0.5f;

    public const float ShuttleLinearDamping = 0.05f;
    public const float ShuttleAngularDamping = 0.05f;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("shuttles");

        InitializeFTL();
        InitializeGridFills();
        InitializeIFF();
        InitializeImpact();

        SubscribeLocalEvent<ShuttleComponent, ComponentAdd>(OnShuttleAdd);
        SubscribeLocalEvent<ShuttleComponent, ComponentStartup>(OnShuttleStartup);
        SubscribeLocalEvent<ShuttleComponent, ComponentShutdown>(OnShuttleShutdown);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        SubscribeLocalEvent<GridInitializeEvent>(OnGridInit);
        SubscribeLocalEvent<GridFixtureChangeEvent>(OnGridFixtureChange);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateHyperspace(frameTime);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        CleanupHyperspace();
    }

    private void OnShuttleAdd(EntityUid uid, ShuttleComponent component, ComponentAdd args)
    {
        // Easier than doing it in the comp and they don't have constructors.
        for (var i = 0; i < component.LinearThrusters.Length; i++)
        {
            component.LinearThrusters[i] = new List<EntityUid>();
        }
    }

    private void OnGridFixtureChange(GridFixtureChangeEvent args)
    {
        // Look this is jank but it's a placeholder until we design it.
        if (args.NewFixtures.Count == 0)
            return;

        var uid = args.NewFixtures[0].Body.Owner;
        var manager = Comp<FixturesComponent>(uid);

        foreach (var fixture in args.NewFixtures)
        {
            _physics.SetDensity(uid, fixture, TileMassMultiplier, false, manager);
            _fixtures.SetRestitution(uid, fixture, 0.1f, false, manager);
        }

        _fixtures.FixtureUpdate(uid, manager: manager);
    }

    private void OnGridInit(GridInitializeEvent ev)
    {
        if (HasComp<MapComponent>(ev.EntityUid))
            return;

        EntityManager.EnsureComponent<ShuttleComponent>(ev.EntityUid);
    }

    private void OnShuttleStartup(EntityUid uid, ShuttleComponent component, ComponentStartup args)
    {
        if (!EntityManager.HasComponent<MapGridComponent>(uid))
        {
            return;
        }

        if (!EntityManager.TryGetComponent(uid, out PhysicsComponent? physicsComponent))
        {
            return;
        }

        if (component.Enabled)
        {
            Enable(uid, physicsComponent);
        }
    }

    public void Toggle(EntityUid uid, ShuttleComponent component)
    {
        if (!EntityManager.TryGetComponent(uid, out PhysicsComponent? physicsComponent))
            return;

        component.Enabled = !component.Enabled;

        if (component.Enabled)
        {
            Enable(uid, physicsComponent);
        }
        else
        {
            Disable(uid, physicsComponent);
        }
    }

    private void Enable(EntityUid uid, PhysicsComponent component)
    {
        FixturesComponent? manager = null;

        _physics.SetBodyType(uid, BodyType.Dynamic, manager: manager, body: component);
        _physics.SetBodyStatus(component, BodyStatus.InAir);
        _physics.SetFixedRotation(uid, false, manager: manager, body: component);
        _physics.SetLinearDamping(component, ShuttleLinearDamping);
        _physics.SetAngularDamping(component, ShuttleAngularDamping);
    }

    private void Disable(EntityUid uid, PhysicsComponent component)
    {
        FixturesComponent? manager = null;

        _physics.SetBodyType(uid, BodyType.Static, manager: manager, body: component);
        _physics.SetBodyStatus(component, BodyStatus.OnGround);
        _physics.SetFixedRotation(uid, true, manager: manager, body: component);
    }

    private void OnShuttleShutdown(EntityUid uid, ShuttleComponent component, ComponentShutdown args)
    {
        // None of the below is necessary for any cleanup if we're just deleting.
        if (EntityManager.GetComponent<MetaDataComponent>(uid).EntityLifeStage >= EntityLifeStage.Terminating)
            return;

        if (!EntityManager.TryGetComponent(uid, out PhysicsComponent? physicsComponent))
        {
            return;
        }

        Disable(uid, physicsComponent);
    }
}
