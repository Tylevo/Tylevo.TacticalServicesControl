using EFT;
using UnityEngine;

namespace TscHh60Visual
{
    internal static class EscortBenchColliderOwner
    {
        internal static Player Resolve(Collider collider, GameWorld world)
        {
            if (collider == null) return null;
            // Mirror native Shot.TryGetBallisticCollider's local-component
            // precedence. A world surface must not borrow a parent's owner;
            // an unbound/unsupported body must not borrow a registry entry.
            var ballistic = collider.GetComponent<BaseBallistic>();
            if (ballistic != null) return (ballistic as BodyPartCollider)?.Player as Player;

            // Native armor plates own central and immediate-child boxes (for
            // example Plate_Korund_chest/Right). SetupBodyPartCollider initializes
            // the parent's bridge and assigns HitCollider to its direct children.
            // Shot resolves one parent when the hit object has no BaseBallistic.
            // Limit that fallback to the verified armor structure, never a
            // recursive search for BodyPartCollider or Player ancestors.
            var parent = collider.transform.parent;
            if (parent != null)
            {
                var plate = parent.GetComponent<BaseBallistic>() as ArmorPlateCollider;
                if (plate != null) return plate.Player as Player;
            }

            // Movement/spirit colliders have no ballistic body component.
            return world != null ? world.GetPlayerByCollider(collider) : null;
        }
    }
}
