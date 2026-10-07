using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace TscHh60Visual
{
    // A single-projectile emitter for both manual and automatic tests. It does not use a hands controller,
    // invent a bot owner, change templates, or add anything to an inventory.
    internal sealed class EscortBenchBallistics : IDisposable
    {
        private const string M33Template = "67dc255ee3028a8b120efc48";
        private const string M21Template = "67dc212493ce32834b0fa446";
        private const string WeaponTemplate = "67d0576f29f580ebc10efd08";
        private const string ObserverHarmonyId = "local.tsc.hh60visual.escortbench.observer";
        private const int MaxPending = 256;
        private static readonly Dictionary<string, PendingShot> Pending = new Dictionary<string, PendingShot>(StringComparer.Ordinal);
        private static readonly List<string> Retired = new List<string>();
        private static readonly HashSet<MethodInfo> ObservedCallbacks = new HashSet<MethodInfo>();
        private static Harmony _observer;
        private static bool _observerWarning;

        private readonly GameWorld _world;
        private readonly Player _caller;
        private readonly string _ownerProfileId;
        private readonly BallisticsCalculator _calculator;
        private readonly ItemFactory _factory;
        private readonly Weapon _weapon;
        private readonly string _benchId = Guid.NewGuid().ToString("N");
        private Ammo _nextAmmo;
        private int _accepted;
        private bool _disposed;

        private sealed class PendingShot
        {
            internal GameWorld World;
            internal Weapon Weapon;
            internal Shot Root;
            internal string BenchId, AmmoId, AmmoTemplate, Owner, IntendedTarget;
            internal int Sequence, Impacts;
            internal float ExpiresAt, ObservedHealthLoss;
            internal bool RootReleased;
        }

        private sealed class HitObservation
        {
            internal PendingShot Pending;
            internal IPlayer Victim;
            internal float HealthBefore;
            internal bool AliveBefore;
            internal int Fragment;
        }

        internal EscortBenchBallistics(Player caller)
        {
            _world = Singleton<GameWorld>.Instance;
            _caller = caller;
            _ownerProfileId = caller == null ? null : caller.ProfileId;
            RequireLocalOwner();
            _calculator = _world.SharedBallisticsCalculator as BallisticsCalculator
                ?? throw new InvalidOperationException("The native shared ballistics calculator is unavailable.");
            _factory = Singleton<ItemFactory>.Instance
                ?? throw new InvalidOperationException("The native item factory is unavailable.");
            _weapon = _factory.CreateItem(MongoID.Generate(), WeaponTemplate, null) as Weapon
                ?? throw new InvalidOperationException("The installed AK-50 weapon template could not be created.");
            EnsureObservers(_world);
        }

        internal string Description => "Native .50 BMG: M33, every fifth M21; one projectile per authorized shot; local player owner; impact/health observation enabled.";

        internal Ammo PeekNextRound()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EscortBenchBallistics));
            RequireLocalOwner();
            if (!ReferenceEquals(_calculator, _world.SharedBallisticsCalculator))
                throw new InvalidOperationException("The shared ballistics calculator changed.");
            if (_nextAmmo != null) return _nextAmmo;
            int sequence = _accepted + 1;
            string template = sequence % 5 == 0 ? M21Template : M33Template;
            var ammo = _factory.CreateItem(MongoID.Generate(), template, null) as Ammo
                ?? throw new InvalidOperationException("The installed .50 BMG ammunition template could not be created.");
            ValidateBallisticTemplate(ammo, template, sequence);
            if (!Finite(ammo.AmmoLifeTimeSec) || ammo.AmmoLifeTimeSec <= 0)
                throw new InvalidOperationException("The installed ammunition has an invalid lifetime.");
            // One exact item is shared by targeting and emission. Failed safety
            // checks do not advance the M33/M21 sequence or manufacture new IDs.
            _nextAmmo = ammo;
            return ammo;
        }

        // True means the native calculator accepted this one projectile. It does
        // not mean a target was hit, damage occurred, or statistics are unaffected.
        internal bool Fire(Vector3 origin, Vector3 direction, string targetProfileId)
        {
            try { return Fire(origin, direction, targetProfileId, PeekNextRound()); }
            catch (Exception error)
            {
                Warn("round preparation failed bench=" + _benchId + " reason=" + error.GetBaseException().Message);
                return false;
            }
        }

        internal bool Fire(Vector3 origin, Vector3 direction, string targetProfileId, Ammo expectedAmmo)
        {
            Shot shot = null;
            PendingShot pending = null;
            int sequence = _accepted + 1;
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(EscortBenchBallistics));
                RequireLocalOwner();
                if (!ReferenceEquals(_calculator, _world.SharedBallisticsCalculator))
                    throw new InvalidOperationException("The shared ballistics calculator changed.");
                if (!Finite(origin) || !Finite(direction) || !Finite(direction.sqrMagnitude) || direction.sqrMagnitude < 0.000001f)
                    throw new ArgumentException("Projectile origin/direction must be finite, with a nonzero direction.");
                if (expectedAmmo == null || !ReferenceEquals(expectedAmmo, _nextAmmo))
                    throw new InvalidOperationException("The predicted round is no longer the emitter's exact next ammunition item.");
                TickObservers();
                if (Pending.Count >= MaxPending) throw new InvalidOperationException("The bench has too many unresolved projectiles.");
                EnsureObservers(_world);
                string template = sequence % 5 == 0 ? M21Template : M33Template;
                // ShotId uses ammo ITEM ID plus FragmentIndex. A fresh item per
                // press prevents this test's shots sharing an ambiguous identity.
                var ammo = expectedAmmo;
                ValidateBallisticTemplate(ammo, template, sequence);
                if (!Finite(ammo.InitialSpeed) || ammo.InitialSpeed <= 0 || !Finite(ammo.AmmoLifeTimeSec) || ammo.AmmoLifeTimeSec <= 0)
                    throw new InvalidOperationException("The installed ammunition has invalid velocity/lifetime values.");
                direction.Normalize();
                float predictedSpeed = ammo.InitialSpeed;
                shot = _calculator.CreateShot(ammo, origin, direction, -1, _ownerProfileId, _weapon, 1f, 0);
                if (shot == null || shot.Player == null || shot.Player.IsAI || !ReferenceEquals(shot.Player.iPlayer, _caller))
                    throw new InvalidOperationException("The native shot did not retain the registered local player owner.");
                if (!ReferenceEquals(shot.Ammo, ammo) || !Finite(shot.StartPosition) || !Finite(shot.StartVelocity) ||
                    !MatchesStock(shot.InitialSpeed, predictedSpeed) || (shot.StartPosition - origin).sqrMagnitude > 0.000001f ||
                    (shot.StartVelocity - direction * predictedSpeed).sqrMagnitude > 0.0001f)
                    throw new InvalidOperationException("The native shot differs from the exact ammunition/start trajectory that was authorized.");
                pending = new PendingShot
                {
                    World = _world, Weapon = _weapon, Root = shot, BenchId = _benchId,
                    AmmoId = ammo.Id, AmmoTemplate = template, Owner = _ownerProfileId,
                    IntendedTarget = targetProfileId ?? string.Empty, Sequence = sequence,
                    ExpiresAt = Time.time + Mathf.Max(5f, ammo.AmmoLifeTimeSec) + 15f
                };
                Pending.Add(pending.AmmoId, pending);
                _calculator.Shoot(shot);
                _accepted = sequence;
                _nextAmmo = null;
                Info("accepted " + Identity(pending) + " fragment=0 owner=" + _ownerProfileId +
                    " intendedTarget=" + pending.IntendedTarget + " weaponTpl=" + WeaponTemplate +
                    " origin=" + Vector(origin) + " direction=" + Vector(direction) +
                    " initialSpeed=" + Number(shot.InitialSpeed) +
                    " path=CreateShot>SharedBallisticsCalculator.Shoot speedFactor=1 fireIndex=-1 hitConfirmed=false");
                return true;
            }
            catch (Exception error)
            {
                // Native Shoot creates a visual before adding to Shots. If that
                // fails, return only our unsubmitted shot; never clear world shots.
                bool queued = shot != null && _calculator != null && _calculator.Shots != null && _calculator.Shots.Contains(shot);
                if (!queued)
                {
                    if (pending != null) Pending.Remove(pending.AmmoId);
                    if (shot != null)
                    {
                        try { Shot.Release(shot); }
                        catch (Exception releaseError) { ObserverWarning(releaseError); }
                    }
                }
                else
                {
                    // A native/other-mod exception after queuing is ambiguous.
                    // Never recycle the queued AmmoId or accept another press.
                    _accepted = Math.Max(_accepted, sequence);
                    _nextAmmo = null;
                    _disposed = true;
                }
                Warn("submission failed bench=" + _benchId + " nativeQueued=" + queued + " reason=" + error.GetBaseException().Message);
                return false;
            }
        }

        private void RequireLocalOwner()
        {
            if (_world == null || !ReferenceEquals(_world, Singleton<GameWorld>.Instance) ||
                _caller == null || _caller.Destroyed || !_caller.IsYourPlayer ||
                !ReferenceEquals(_world.MainPlayer, _caller) || string.IsNullOrWhiteSpace(_ownerProfileId) ||
                _caller.HealthController == null || !_caller.HealthController.IsAlive)
                throw new InvalidOperationException("A live local main player in the current raid is required.");
            var bridge = _world.GetEverExistedBridgeByProfileID(_ownerProfileId);
            if (bridge == null || bridge.IsAI || !ReferenceEquals(bridge.iPlayer, _caller))
                throw new InvalidOperationException("The local player has no matching registered ballistic owner bridge.");
        }

        private void ValidateBallisticTemplate(Ammo ammo, string template, int sequence)
        {
            // The clearance envelope was checked for these exact stock SPT 4.1.5
            // trajectories. Mods can change live templates after loading the DB;
            // inspect the actual created Ammo and reject unsupported values.
            bool tracer = template == M21Template;
            float expectedSpeed = tracer ? 867f : 887f;
            float expectedMass = tracer ? 45.3f : 42.8f;
            float expectedDiameter = 12.7f;
            float expectedCoefficient = tracer ? 0.614f : 0.622f;
            bool supported = (tracer || template == M33Template) &&
                MatchesStock(ammo.InitialSpeed, expectedSpeed) &&
                MatchesStock(ammo.BulletMassGram, expectedMass) &&
                MatchesStock(ammo.BulletDiameterMilimeters, expectedDiameter) &&
                MatchesStock(ammo.BallisticCoeficient, expectedCoefficient);
            Info("ballistics-check bench=" + _benchId + " seq=" + sequence + " ammoItem=" + ammo.Id +
                " ammoTpl=" + template + " supported=" + supported +
                " initialSpeed=" + ExactNumber(ammo.InitialSpeed) +
                " massGram=" + ExactNumber(ammo.BulletMassGram) +
                " diameterMm=" + ExactNumber(ammo.BulletDiameterMilimeters) +
                " ballisticCoefficient=" + ExactNumber(ammo.BallisticCoeficient) +
                " ammoLifetime=" + ExactNumber(ammo.AmmoLifeTimeSec));
            if (!supported)
                throw new InvalidOperationException("Loaded ammunition differs from the verified bench trajectory: expected speed=" +
                    ExactNumber(expectedSpeed) + ", massGram=" + ExactNumber(expectedMass) +
                    ", diameterMm=" + ExactNumber(expectedDiameter) + ", ballisticCoefficient=" + ExactNumber(expectedCoefficient) + ".");
        }

        private static bool MatchesStock(float actual, float expected)
        {
            // Allow floating-point conversion noise, not gameplay tuning changes.
            return Finite(actual) && Math.Abs((double)actual - expected) <= Math.Max(0.000001, Math.Abs((double)expected) * 0.000001);
        }

        public void Dispose()
        {
            _disposed = true;
            _nextAmmo = null;
            // Recall closes this emitter. Already submitted native projectiles
            // keep their observations until release, expiry, or world disposal.
        }

        internal static void TickObservers()
        {
            try
            {
                Retired.Clear();
                // Native flight time pauses with gameplay. Pausing the raid must
                // not expire the observer while its projectile is still in flight.
                float now = Time.time;
                var world = Singleton<GameWorld>.Instance;
                foreach (var pair in Pending)
                {
                    var pending = pair.Value;
                    if (pending.World == null || !ReferenceEquals(pending.World, world) || now >= pending.ExpiresAt)
                    {
                        Complete(pending, pending.World == null || !ReferenceEquals(pending.World, world) ? "world-disposed" : "observation-expired");
                        Retired.Add(pair.Key);
                    }
                }
                foreach (var id in Retired) Pending.Remove(id);
                Retired.Clear();
            }
            catch (Exception error) { ObserverWarning(error); }
        }

        internal static void ShutdownObservers()
        {
            foreach (var pending in Pending.Values) Complete(pending, "observer-shutdown");
            Pending.Clear();
            Retired.Clear();
            ObservedCallbacks.Clear();
            try { _observer?.UnpatchSelf(); }
            catch (Exception error) { ObserverWarning(error); }
            _observer = null;
            _observerWarning = false;
        }

        private static void EnsureObservers(GameWorld world)
        {
            // ClientLocalGameWorld inherits ClientGameWorld's override, which
            // does not call the GameWorld base implementation. Observe the actual
            // world's resolved virtual method rather than only that base method.
            MethodInfo callback = AccessTools.Method(world.GetType(), "ShotDelegate", new[] { typeof(Shot) });
            // Resolve on its declaring type, not an inherited reflected wrapper.
            // Preserve ClientGameWorld's implementation rather than its base definition.
            if (callback != null)
                callback = AccessTools.DeclaredMethod(callback.DeclaringType, "ShotDelegate", new[] { typeof(Shot) });
            if (_observer != null && callback != null && ObservedCallbacks.Contains(callback)) return;
            MethodInfo release = AccessTools.Method(typeof(Shot), "Release", new[] { typeof(Shot) });
            if (callback == null || release == null) throw new MissingMethodException("Verified native shot observation methods are unavailable.");
            bool firstObserver = _observer == null;
            var harmony = _observer ?? new Harmony(ObserverHarmonyId);
            try
            {
                harmony.Patch(callback,
                    prefix: new HarmonyMethod(typeof(EscortBenchBallistics), nameof(BeforeNativeHit)),
                    postfix: new HarmonyMethod(typeof(EscortBenchBallistics), nameof(AfterNativeHit)),
                    finalizer: new HarmonyMethod(typeof(EscortBenchBallistics), nameof(NativeHitFailure)));
                if (firstObserver) harmony.Patch(release, prefix: new HarmonyMethod(typeof(EscortBenchBallistics), nameof(BeforeRelease)));
                ObservedCallbacks.Add(callback);
                _observer = harmony;
                Info("observer ready callback=" + callback.DeclaringType.FullName + "." + callback.Name + " exactAmmoIdentityFilter=true");
            }
            catch
            {
                if (firstObserver) harmony.UnpatchSelf();
                else harmony.Unpatch(callback, HarmonyPatchType.All, ObserverHarmonyId);
                throw;
            }
        }

        private static bool FindPending(Shot shot, out PendingShot pending)
        {
            pending = null;
            if (shot == null || shot.Ammo == null || !Pending.TryGetValue(shot.Ammo.Id, out pending)) return false;
            if (!ReferenceEquals(shot.Weapon, pending.Weapon) || !string.Equals(shot.PlayerProfileID, pending.Owner, StringComparison.Ordinal))
            { pending = null; return false; }
            return true;
        }

        private static void BeforeNativeHit(GameWorld __instance, Shot __0, out HitObservation __state)
        {
            __state = null;
            try
            {
                PendingShot pending;
                if (!FindPending(__0, out pending) || !ReferenceEquals(__instance, pending.World)) return;
                bool impact = !__0.IsFlyingOutOfTime && __0.HittedBallisticCollider != null && __0.HitCollider != null;
                if (impact) pending.Impacts++;
                var body = __0.HittedBallisticCollider as BodyPartCollider;
                // This is the same body bridge used by native ApplyHit. Do not
                // infer a victim from a nearby transform or the intended target.
                var victim = body == null ? null : body.Player;
                __state = new HitObservation
                {
                    Pending = pending, Victim = victim, Fragment = __0.FragmentIndex,
                    HealthBefore = Health(victim), AliveBefore = victim?.HealthController?.IsAlive ?? false
                };
                Info("native-callback " + Identity(pending) + " fragment=" + __0.FragmentIndex +
                    " impactObserved=" + impact + " victim=" + (victim?.ProfileId ?? "<none>") +
                    " bodyPart=" + (body == null ? "<none>" : body.BodyPartType.ToString()) +
                    " collider=" + Path(__0.HitCollider == null ? null : __0.HitCollider.transform) +
                    " hitPoint=" + Vector(__0.HitPoint) + " nativeDamageInput=" + Number(__0.Damage) +
                    " outOfTime=" + __0.IsFlyingOutOfTime + " avoidAdditionalDamage=" + __0.AvoidAdditionalDamage);
            }
            catch (Exception error) { ObserverWarning(error); }
        }

        private static void AfterNativeHit(HitObservation __state)
        {
            if (__state == null) return;
            try
            {
                float after = Health(__state.Victim);
                bool measured = Finite(__state.HealthBefore) && Finite(after);
                float loss = measured ? Mathf.Max(0, __state.HealthBefore - after) : 0;
                __state.Pending.ObservedHealthLoss += loss;
                Info("native-result " + Identity(__state.Pending) + " fragment=" + __state.Fragment +
                    " victim=" + (__state.Victim?.ProfileId ?? "<none>") +
                    " healthBefore=" + Number(__state.HealthBefore) + " healthAfter=" + Number(after) +
                    " healthLossObserved=" + (measured ? Number(loss) : "unavailable") +
                    " bodyDamageObserved=" + (measured && loss > 0) +
                    " aliveBefore=" + __state.AliveBefore + " aliveAfter=" + (__state.Victim?.HealthController?.IsAlive ?? false));
            }
            catch (Exception error) { ObserverWarning(error); }
        }

        private static Exception NativeHitFailure(Exception __exception, HitObservation __state)
        {
            try
            {
                if (__exception != null && __state != null)
                    Warn("native callback failed " + Identity(__state.Pending) + " reason=" + __exception.GetBaseException().Message);
            }
            catch (Exception error) { ObserverWarning(error); }
            return __exception; // Preserve native/other-mod exception behavior.
        }

        private static void BeforeRelease(Shot __0)
        {
            try
            {
                PendingShot pending;
                if (!FindPending(__0, out pending) || !ReferenceEquals(__0, pending.Root)) return;
                pending.RootReleased = true;
                pending.Root = null;
                // Vanilla normally releases after all fragments finish. Keep
                // the unique ammo identity through the lifetime window anyway,
                // so neither pooling nor other release hooks lose late evidence.
                Info("native-root-released " + Identity(pending) + " observerRetained=true");
            }
            catch (Exception error) { ObserverWarning(error); }
        }

        private static void Complete(PendingShot pending, string reason)
        {
            Info("complete " + Identity(pending) + " reason=" + reason + " observedImpacts=" + pending.Impacts +
                " observedHealthLoss=" + Number(pending.ObservedHealthLoss) + " nativeRootReleased=" + pending.RootReleased);
        }

        private static float Health(IPlayer victim)
        {
            return victim?.HealthController == null ? float.NaN : victim.HealthController.GetBodyPartHealth(EBodyPart.Common, false).Current;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        private static string Number(float value) { return Finite(value) ? value.ToString("0.###", CultureInfo.InvariantCulture) : "unavailable"; }
        private static string ExactNumber(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string Vector(Vector3 value) { return "(" + Number(value.x) + "," + Number(value.y) + "," + Number(value.z) + ")"; }
        private static string Identity(PendingShot shot) { return "bench=" + shot.BenchId + " seq=" + shot.Sequence + " ammoItem=" + shot.AmmoId + " ammoTpl=" + shot.AmmoTemplate; }

        private static string Path(Transform transform)
        {
            if (transform == null) return "<none>";
            var path = new StringBuilder(transform.name);
            int depth = 0;
            while (transform.parent != null && depth++ < 32) { transform = transform.parent; path.Insert(0, transform.name + "/"); }
            return path.ToString();
        }

        private static void ObserverWarning(Exception error)
        {
            if (_observerWarning) return;
            _observerWarning = true;
            Warn("observation diagnostic failed; native processing continues: " + error.GetBaseException().Message);
        }

        private static void Info(string message) { try { Plugin.Log?.LogInfo("[EscortBench] " + message); } catch { } }
        private static void Warn(string message) { try { Plugin.Log?.LogWarning("[EscortBench] " + message); } catch { } }
    }
}
