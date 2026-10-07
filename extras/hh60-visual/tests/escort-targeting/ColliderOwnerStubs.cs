using System.Collections.Generic;

// Minimal native API stubs exercise the actual production resolver without
// distributing or loading Unity/EFT binaries. Body hitboxes intentionally are
// absent from the movement-collider registry, matching GameWorld.RegisterPlayer.
namespace UnityEngine
{
    public class Collider
    {
        public object Component;
        public readonly Transform transform = new Transform();
        public T GetComponent<T>() where T : class => Component as T;
    }
    public class Transform
    {
        public object Component;
        public Transform parent;
        public T GetComponent<T>() where T : class => Component as T;
    }
}

namespace EFT
{
    public interface IPlayer { }
    public class Player : IPlayer { }
    public class GameWorld
    {
        public readonly Dictionary<UnityEngine.Collider, IPlayer> PlayersColliders = new Dictionary<UnityEngine.Collider, IPlayer>();
        public int LookupCount;
        public Player GetPlayerByCollider(UnityEngine.Collider collider)
        {
            LookupCount++;
            IPlayer player;
            return PlayersColliders.TryGetValue(collider, out player) ? player as Player : null;
        }
    }
}

public class BaseBallistic { }
public class BodyPartCollider : BaseBallistic { public EFT.IPlayer Player { get; set; } }
public class ArmorPlateCollider : BodyPartCollider { }
public class UnsupportedObserver : EFT.IPlayer { }
