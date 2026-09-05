namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// A click belongs to the region and rendered view where its press began.
/// Every release consumes that press, including a release outside the phone.
/// </summary>
public sealed class PhonePointerGesture
{
	private bool _armed;
	private int _regionId = -1;
	private int _viewGeneration;

	public void BeginPress(int regionId, int viewGeneration)
	{
		Cancel();
		if (regionId < 0)
		{
			return;
		}

		_regionId = regionId;
		_viewGeneration = viewGeneration;
		_armed = true;
	}

	public bool EndPress(int regionId, int viewGeneration)
	{
		bool clicked = _armed && regionId >= 0 &&
		               regionId == _regionId && viewGeneration == _viewGeneration;
		Cancel();
		return clicked;
	}

	public void Cancel()
	{
		_armed = false;
		_regionId = -1;
		_viewGeneration = 0;
	}
}
