using Comfort.Common;
using EFT;
using EFT.UI;
using System.Collections;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public class HeliExfiltrationPoint : MonoBehaviour, IPhysicsTrigger
{
	private float _timer;
	private Coroutine _coroutine;
	private BattleUIPanelExitTrigger _battleUIPanelExitTrigger;
	private GameWorld _gameWorld;

	public string Description => "HeliExfiltrationPoint";

	private void Start()
	{
		_gameWorld = Singleton<GameWorld>.Instance;
		_battleUIPanelExitTrigger = Singleton<GameUI>.Instance?.BattleUiPanelExitTrigger;
	}

	public void OnTriggerEnter(Collider collider)
	{
		Player player = _gameWorld?.GetPlayerByCollider(collider);
		if (player == null || !player.IsYourPlayer)
		{
			return;
		}

		ResetTimer();
		_battleUIPanelExitTrigger?.Show(_timer);

		if (_coroutine == null)
		{
			_coroutine = StartCoroutine(Timer(player));
		}
	}

	public void OnTriggerExit(Collider collider)
	{
		Player player = _gameWorld?.GetPlayerByCollider(collider);
		if (player == null || !player.IsYourPlayer)
		{
			return;
		}

		ResetTimer();
		_battleUIPanelExitTrigger?.Close();

		if (_coroutine != null)
		{
			StopCoroutine(_coroutine);
			_coroutine = null;
		}
	}

	private void OnDestroy()
	{
		if (_coroutine != null)
		{
			StopCoroutine(_coroutine);
			_coroutine = null;
		}

		if (Singleton<GameUI>.Instantiated && _battleUIPanelExitTrigger != null)
		{
			_battleUIPanelExitTrigger.Close();
		}
	}

	private void ResetTimer()
	{
		_timer = FireSupportTuningSettings.GetHelicopterExtractTime();
	}

	private IEnumerator Timer(Player player)
	{
		while (_timer > 0)
		{
			yield return null;
			_timer -= Time.deltaTime;
		}

		_battleUIPanelExitTrigger?.Close();
		_coroutine = null;

		// In a Fika session the extraction must go through Fika's extract flow
		// (host stays to keep the session alive, clients despawn cleanly).
		// Stopping the session directly here put the lobby into limbo when the
		// host extracted before other players.
		if (FireSupportExtraction.TryOverrideExtract(player, "UH-60 Black Hawk"))
		{
			yield break;
		}

		if (Singleton<AbstractGame>.Instance is ISessionStopper sessionStopper)
		{
			sessionStopper.StopSession(player.ProfileId, ExitStatus.Survived, "UH-60 Black Hawk");
		}
	}
}
