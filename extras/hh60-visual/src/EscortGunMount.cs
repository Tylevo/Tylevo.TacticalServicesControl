using System;
using UnityEngine;

namespace TscHh60Visual
{
    // Shared visible-gun mechanics for the stationary and moving manual benches.
    // Muzzle and Barrel always come from the achieved transform, never the aim goal.
    internal sealed class EscortGunMount
    {
        private readonly Transform _mount, _aircraft;
        private readonly Quaternion _mountRest, _barrelRest;
        private readonly Vector3 _tip;
        internal readonly Transform BarrelTransform;
        internal readonly Vector3 RestHeading;
        internal bool Exists => _mount != null && BarrelTransform != null && _mount.parent != null;
        internal bool Available => Exists && _mount.gameObject.activeInHierarchy && BarrelTransform.gameObject.activeInHierarchy;
        internal Vector3 Muzzle => BarrelTransform.TransformPoint(_tip);
        internal Vector3 Barrel => BarrelTransform.TransformDirection(Vector3.up).normalized;

        internal EscortGunMount(Transform mount, Transform barrel, Vector3 tip, Transform aircraft)
        {
            _mount = mount;
            _aircraft = aircraft;
            BarrelTransform = barrel;
            _mountRest = mount.localRotation;
            _barrelRest = barrel.localRotation;
            _tip = tip;
            var forward = Vector3.ProjectOnPlane(Barrel, aircraft.up).normalized;
            if (forward.sqrMagnitude < 0.9f) throw new InvalidOperationException("Unsupported HH60 gun rest axis");
            RestHeading = aircraft.InverseTransformDirection(forward);
        }

        internal void Angles(Vector3 direction, out float yaw, out float pitch)
        {
            var up = _aircraft.up;
            var flat = Vector3.ProjectOnPlane(direction, up);
            var restHeading = _aircraft.TransformDirection(RestHeading);
            yaw = flat.sqrMagnitude > 0.000001f ? Vector3.SignedAngle(restHeading, flat, up) : 180f;
            pitch = Mathf.Atan2(Vector3.Dot(direction, up), flat.magnitude) * Mathf.Rad2Deg;
        }

        internal void Track(float yaw, float pitch, float dt, Transform aircraft)
        {
            var up = aircraft.up;
            var restWorld = _mount.parent.rotation * _mountRest;
            var goalMount = Quaternion.AngleAxis(yaw, up) * restWorld;
            _mount.rotation = Quaternion.RotateTowards(_mount.rotation, goalMount, 65f * dt);
            var baseBarrelRotation = _mount.rotation * _barrelRest;
            var baseAxis = baseBarrelRotation * Vector3.up;
            var horizontal = Vector3.ProjectOnPlane(baseAxis, up).normalized;
            float radians = pitch * Mathf.Deg2Rad;
            var desired = horizontal * Mathf.Cos(radians) + up * Mathf.Sin(radians);
            var goalBarrel = Quaternion.FromToRotation(baseAxis, desired) * baseBarrelRotation;
            BarrelTransform.rotation = Quaternion.RotateTowards(BarrelTransform.rotation, goalBarrel, 90f * dt);
        }

        internal void Restore()
        {
            if (_mount != null) _mount.localRotation = _mountRest;
            if (BarrelTransform != null) BarrelTransform.localRotation = _barrelRest;
        }
    }
}
