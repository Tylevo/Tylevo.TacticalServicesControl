using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using System;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public class VehicleWeapon
{
	private readonly string _playerProfileId;
	private readonly BallisticsCalculator _ballisticsCalculator;

	private readonly Weapon _weapon;
	private readonly Ammo _ammoItem;

	public readonly int fireRate;
	public readonly float timeBetweenShots;

	public VehicleWeapon(string playerProfileId, string weaponTpl, string ammoTpl)
	{
		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		if (gameWorld == null)
		{
			throw new NullReferenceException("GameWorld is null");
		}

		_playerProfileId = playerProfileId?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(_playerProfileId) ||
		    gameWorld.GetEverExistedBridgeByProfileID(_playerProfileId) == null)
		{
			throw new InvalidOperationException(
				$"Ballistic owner profile '{_playerProfileId}' is not bound to a live or previously registered EFT player bridge.");
		}
		ItemFactory itemFactory = Singleton<ItemFactory>.Instance
			?? throw new NullReferenceException("ItemFactory is null");
		_ballisticsCalculator = (BallisticsCalculator)gameWorld.SharedBallisticsCalculator;

		_weapon = (Weapon)itemFactory.CreateItem(MongoID.Generate(), weaponTpl, null);
		_ammoItem = (Ammo)itemFactory.CreateItem(MongoID.Generate(), ammoTpl, null);
		fireRate = _weapon.FireRate;
		timeBetweenShots = 1f / (fireRate / 60f);
	}

	public A10EftTrajectoryEvaluator CreateTrajectoryEvaluator()
	{
		IObserverToPlayerBridge owner = Singleton<GameWorld>.Instance?.GetEverExistedBridgeByProfileID(_playerProfileId);
		bool isBotShot = owner != null && owner.IsAI && !_weapon.IsGrenadeLauncher;
		return CreateTrajectoryEvaluator(_weapon, _ammoItem, isBotShot);
	}

	public static A10EftTrajectoryEvaluator CreateGau8VisualTrajectoryEvaluator()
	{
		// Visual-only clients do not need to register or fire an unowned projectile
		// to read the same ammunition and weapon parameters as the authority.
		ItemFactory factory = Singleton<ItemFactory>.Instance
			?? throw new NullReferenceException("ItemFactory is null");
		var weapon = (Weapon)factory.CreateItem(MongoID.Generate(), ItemConstants.GAU8_WEAPON_TPL, null);
		var ammo = (Ammo)factory.CreateItem(MongoID.Generate(), ItemConstants.GAU8_AMMO_TPL, null);
		return CreateTrajectoryEvaluator(weapon, ammo, false);
	}

	private static A10EftTrajectoryEvaluator CreateTrajectoryEvaluator(Weapon weapon, Ammo ammo, bool isBotShot)
	{
		return new A10EftTrajectoryEvaluator(ammo.InitialSpeed * weapon.SpeedFactor,
			ammo.BulletMassGram, ammo.BulletDiameterMilimeters, ammo.BallisticCoeficient,
			ammo.AmmoLifeTimeSec, isBotShot);
	}

	public Shot FireProjectile(Vector3 origin, Vector3 direction)
	{
		// fireIndex seems to be related to player statistics - counting the number of shots player has fired
		// Leave fireIndex at -1 because we don't want vehicle weapon shots inflating player statistics
		Shot bullet = _ballisticsCalculator.CreateShot(_ammoItem, origin, direction, -1, _playerProfileId,
			_weapon, _weapon.SpeedFactor);
		_ballisticsCalculator.Shoot(bullet);
		return bullet;
	}
}
