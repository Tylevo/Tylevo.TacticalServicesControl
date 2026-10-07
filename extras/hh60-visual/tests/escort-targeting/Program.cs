using System;
using TscHh60Visual;

internal static class Program
{
    private static int _checks;

    private static void Main()
    {
        ColliderOwnership();
        // A known enemy is eligible only if every live/native relationship guard
        // agrees. No side/faction string or random policy enters this decision.
        Check(EscortBenchSafetyPolicy.ConfirmedHostile(true, true, true, true, true, false, false), "recorded active hostile AI");
        Check(!EscortBenchSafetyPolicy.ConfirmedHostile(true, false, true, true, true, false, false), "human protected even if recorded enemy");
        Check(!EscortBenchSafetyPolicy.ConfirmedHostile(false, true, true, true, true, false, false), "dead excluded");
        Check(!EscortBenchSafetyPolicy.ConfirmedHostile(true, true, false, true, true, false, false), "inactive/unsupported bot excluded");
        Check(!EscortBenchSafetyPolicy.ConfirmedHostile(true, true, true, false, true, false, false), "unknown group excluded");
        Check(!EscortBenchSafetyPolicy.ConfirmedHostile(true, true, true, true, false, false, false), "neutral or unrecorded relationship excluded");
        Check(!EscortBenchSafetyPolicy.ConfirmedHostile(true, true, true, true, true, true, false), "explicit ally overrides enemy record");
        Check(!EscortBenchSafetyPolicy.ConfirmedHostile(true, true, true, true, true, false, true), "shared caller group protected");

        Limits(12f, -55f, -65f, true);
        Limits(200f, 55f, 10f, true);
        Limits(11.99f, 0f, 0f, false);
        Limits(200.01f, 0f, 0f, false);
        Limits(80f, -55.01f, 0f, false);
        Limits(80f, 55.01f, 0f, false);
        Limits(80f, 180f, 0f, false);
        Limits(80f, 0f, -65.01f, false);
        Limits(80f, 0f, 10.01f, false);
        Limits(float.NaN, 0f, 0f, false);
        Limits(80f, float.PositiveInfinity, 0f, false);
        Limits(80f, 0f, float.NegativeInfinity, false);
        Check(EscortBenchSafetyPolicy.Aligned(0.15f), "achieved barrel alignment boundary");
        Check(!EscortBenchSafetyPolicy.Aligned(0.151f), "shot refused while tracking");
        Check(!EscortBenchSafetyPolicy.Aligned(float.NaN), "invalid achieved axis refused");
        Check(EscortBenchSafetyPolicy.RelevantCollision(false, false, false), "solid world collider blocks");
        Check(EscortBenchSafetyPolicy.RelevantCollision(true, true, false), "trigger player hitbox blocks");
        Check(EscortBenchSafetyPolicy.RelevantCollision(true, false, true), "ballistic trigger blocks");
        Check(!EscortBenchSafetyPolicy.RelevantCollision(true, false, false), "unrelated trigger zone ignored");

        Check(EscortBenchSafetyPolicy.IntersectsProtectedCorridor(30f, 0f, 1f), "body before target blocks");
        Check(EscortBenchSafetyPolicy.IntersectsProtectedCorridor(260f, 0f, 1f), "body beyond 200m target limit still blocks");
        Check(EscortBenchSafetyPolicy.IntersectsProtectedCorridor(1200f, 30f, 1f), "distant drop/dispersion envelope protects bodies");
        Check(!EscortBenchSafetyPolicy.IntersectsProtectedCorridor(30f, 20f, 1f), "clear lateral body does not block");
        Check(!EscortBenchSafetyPolicy.IntersectsProtectedCorridor(-20f, 0f, 1f), "body wholly behind muzzle does not block");
        Check(EscortBenchSafetyPolicy.IntersectsProtectedCorridor(-0.2f, 0f, 1f), "body overlapping muzzle plane blocks");
        Check(EscortBenchSafetyPolicy.IntersectsProtectedCorridor(float.NaN, 0f, 1f), "unknown player position fails closed");
        Check(EscortBenchSafetyPolicy.IntersectsProtectedCorridor(30f, float.NaN, 1f), "invalid corridor distance fails closed");
        Check(EscortBenchSafetyPolicy.IntersectsProtectedCorridor(30f, 0f, -1f), "invalid body size fails closed");

        float previous = 0f;
        for (int distance = 0; distance <= 200; distance += 25)
        {
            float clearance = EscortBenchSafetyPolicy.WorldClearance(distance);
            Check(clearance >= previous, "swept clearance grows with distance");
            float vacuumDropForSlowestAmmo = 0.5f * 9.81f * (distance / 867f) * (distance / 867f);
            Check(clearance > vacuumDropForSlowestAmmo + 0.09f, "clearance exceeds M21 vacuum drop");
            previous = clearance;
        }
        Throws(() => EscortBenchSafetyPolicy.WorldClearance(201f), "path sweep limited to bench range");
        Throws(() => EscortBenchSafetyPolicy.WorldClearance(float.NaN), "invalid path length rejected");
        Console.WriteLine(_checks + " escort targeting policy checks passed.");
    }

    private static void ColliderOwnership()
    {
        var world = new EFT.GameWorld();
        var target = new EFT.Player();
        var other = new EFT.Player();
        var torso = new UnityEngine.Collider { Component = new BodyPartCollider { Player = target } };
        Check(!world.PlayersColliders.ContainsKey(torso), "regression fixture: native torso absent from controller registry");
        Check(ReferenceEquals(EscortBenchColliderOwner.Resolve(torso, world), target), "native body bridge resolves unregistered target hitbox");
        Check(world.LookupCount == 0, "body bridge does not depend on controller registry");
        var armor = new UnityEngine.Collider { Component = new ArmorPlateCollider { Player = target } };
        Check(ReferenceEquals(EscortBenchColliderOwner.Resolve(armor, world), target), "derived armor plate resolves target body");
        world.PlayersColliders[torso] = other;
        Check(ReferenceEquals(EscortBenchColliderOwner.Resolve(torso, world), target), "exact hitbox bridge wins over conflicting controller map");
        var friendly = new UnityEngine.Collider { Component = new BodyPartCollider { Player = other } };
        Check(!ReferenceEquals(EscortBenchColliderOwner.Resolve(friendly, world), target), "another body remains a blocker");
        var controller = new UnityEngine.Collider();
        world.PlayersColliders[controller] = target;
        Check(ReferenceEquals(EscortBenchColliderOwner.Resolve(controller, world), target), "movement/spirit controller retains native registry fallback");
        Check(EscortBenchColliderOwner.Resolve(new UnityEngine.Collider(), world) == null, "unregistered world geometry remains a blocker");
        var unbound = new UnityEngine.Collider { Component = new BodyPartCollider() };
        Check(EscortBenchColliderOwner.Resolve(unbound, world) == null, "unbound body without registry owner fails closed");
        world.PlayersColliders[unbound] = target;
        Check(EscortBenchColliderOwner.Resolve(unbound, world) == null, "unbound body cannot borrow ownership from stale controller registry");
        var unsupported = new UnityEngine.Collider { Component = new BodyPartCollider { Player = new UnsupportedObserver() } };
        world.PlayersColliders[unsupported] = target;
        Check(EscortBenchColliderOwner.Resolve(unsupported, world) == null, "unsupported authoritative body owner cannot be replaced by stale registry owner");
        Check(EscortBenchColliderOwner.Resolve(null, world) == null, "null collision fails closed");
        Check(EscortBenchColliderOwner.Resolve(controller, null) == null, "controller with unavailable world fails closed");
        Check(ReferenceEquals(EscortBenchColliderOwner.Resolve(torso, null), target), "authoritative body bridge is self-contained");

        // Native plate parents carry ArmorPlateCollider; their Left/Right/Top
        // child boxes carry no ballistic component or controller registration.
        var plateRoot = new UnityEngine.Transform { Component = new ArmorPlateCollider { Player = target } };
        var plateChild = new UnityEngine.Collider();
        plateChild.transform.parent = plateRoot;
        Check(!world.PlayersColliders.ContainsKey(plateChild), "regression fixture: armor side box absent from controller registry");
        Check(ReferenceEquals(EscortBenchColliderOwner.Resolve(plateChild, world), target), "immediate armor child resolves native plate owner");
        Check(ReferenceEquals(EscortBenchColliderOwner.Resolve(plateChild, null), target), "armor parent bridge is self-contained");
        world.PlayersColliders[plateChild] = other;
        Check(ReferenceEquals(EscortBenchColliderOwner.Resolve(plateChild, world), target), "armor bridge precedes stale controller ownership");
        plateChild.Component = new BodyPartCollider { Player = other };
        Check(ReferenceEquals(EscortBenchColliderOwner.Resolve(plateChild, world), other), "local body ownership precedes armor parent");
        plateChild.Component = new BaseBallistic();
        Check(EscortBenchColliderOwner.Resolve(plateChild, world) == null, "local world ballistic surface cannot borrow armor parent or registry owner");
        plateChild.Component = new BodyPartCollider();
        Check(EscortBenchColliderOwner.Resolve(plateChild, world) == null, "unbound local body cannot borrow armor parent");
        plateChild.Component = null;
        plateRoot.Component = new ArmorPlateCollider();
        Check(EscortBenchColliderOwner.Resolve(plateChild, world) == null, "unbound parent plate cannot borrow registry owner");
        plateRoot.Component = new ArmorPlateCollider { Player = new UnsupportedObserver() };
        Check(EscortBenchColliderOwner.Resolve(plateChild, world) == null, "unsupported parent plate owner fails closed");
        world.PlayersColliders.Remove(plateChild);
        plateRoot.Component = new BodyPartCollider { Player = target };
        Check(EscortBenchColliderOwner.Resolve(plateChild, world) == null, "ordinary body ancestor is not an armor child binding");
        plateRoot.Component = new BaseBallistic();
        Check(EscortBenchColliderOwner.Resolve(plateChild, world) == null, "world ballistic parent remains a blocker");
        plateRoot.Component = null;
        plateRoot.parent = new UnityEngine.Transform { Component = new ArmorPlateCollider { Player = target } };
        Check(EscortBenchColliderOwner.Resolve(plateChild, world) == null, "armor grandparent is not searched recursively");
    }

    private static void Limits(float range, float yaw, float pitch, bool expected)
    {
        string reason;
        bool actual = EscortBenchSafetyPolicy.WithinLimits(range, yaw, pitch, out reason);
        Check(actual == expected && (actual ? reason == null : !string.IsNullOrEmpty(reason)), "arc/range decision and diagnostic");
    }

    private static void Throws(Action action, string label)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { _checks++; return; }
        throw new Exception(label);
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        _checks++;
    }
}
