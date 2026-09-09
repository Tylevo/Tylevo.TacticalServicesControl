using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Carries the phone's authored grip onto the rendered first-person palm after
/// EFT has finished its movement animation and IK. Only the phone prop moves;
/// the usable-item rig and its animation bindings keep their original hierarchy.
/// </summary>
internal sealed class UavPhoneGripBinding : IDisposable
{
	private const string LeftPalmName = "Base HumanLPalm";
	private readonly Transform _phone;
	private readonly Transform _authoredPalm;
	private readonly Transform _renderedPalm;
	private Vector3 _authoredLocalPosition;
	private Quaternion _authoredLocalRotation;
	private bool _poseCaptured;
	private bool _disposed;

	private UavPhoneGripBinding(Transform phone, Transform authoredPalm, Transform renderedPalm)
	{
		_phone = phone;
		_authoredPalm = authoredPalm;
		_renderedPalm = renderedPalm;
	}

	public static UavPhoneGripBinding TryCreate(Player owner, Animator phoneAnimator)
	{
		if (owner?.IsYourPlayer != true || phoneAnimator == null ||
		    owner.PlayerBody?.BodySkins == null ||
		    !owner.PlayerBody.BodySkins.TryGetValue(EBodyModelPart.Hands, out EFT.Visual.LoddedSkin handsSkin) ||
		    handsSkin == null)
		{
			return null;
		}

		Transform animatorRoot = phoneAnimator.transform;
		Transform phone = FindUniqueDescendant(animatorRoot, "Phone");
		Transform authoredPalm = FindUniqueDescendant(animatorRoot, LeftPalmName);
		if (phone == null || authoredPalm == null || authoredPalm.IsChildOf(phone) ||
		    FindUniqueDescendant(phone, "Screen")?.GetComponent<Renderer>() == null)
		{
			return null;
		}

		var palms = new HashSet<Transform>();
		foreach (Renderer renderer in handsSkin.GetRenderers())
		{
			if (renderer is not SkinnedMeshRenderer skinnedRenderer || skinnedRenderer == null)
			{
				continue;
			}

			foreach (Transform bone in skinnedRenderer.bones)
			{
				if (bone == null || bone.name != LeftPalmName || bone == authoredPalm ||
				    bone == phone || bone.IsChildOf(animatorRoot))
				{
					continue;
				}

				palms.Add(bone);
			}
		}

		// Several LOD renderers may share one palm. Distinct palms are ambiguous:
		// leave the original presentation in place rather than choosing a rig.
		if (palms.Count != 1)
		{
			return null;
		}

		Transform renderedPalm = null;
		foreach (Transform palm in palms)
		{
			renderedPalm = palm;
		}

		TscDiagnostics.LogPhone(
			$"TSC upright phone grip bound. phone={GetPath(phone)}, authoredPalm={GetPath(authoredPalm)}, renderedPalm={GetPath(renderedPalm)}.");
		return new UavPhoneGripBinding(phone, authoredPalm, renderedPalm);
	}

	public void Apply()
	{
		// onBeforeRender can be invoked more than once in a frame. Always start
		// from the authored pose so repeated callbacks cannot compound the delta.
		RestoreAuthoredPose();
		if (_disposed || _phone == null || _authoredPalm == null || _renderedPalm == null)
		{
			return;
		}

		Vector3 gripPosition = _authoredPalm.InverseTransformPoint(_phone.position);
		Quaternion gripRotation = Quaternion.Inverse(_authoredPalm.rotation) * _phone.rotation;
		Vector3 renderedPosition = _renderedPalm.TransformPoint(gripPosition);
		Quaternion renderedRotation = _renderedPalm.rotation * gripRotation;

		_authoredLocalPosition = _phone.localPosition;
		_authoredLocalRotation = _phone.localRotation;
		_poseCaptured = true;
		_phone.SetPositionAndRotation(renderedPosition, renderedRotation);
	}

	public void RestoreAuthoredPose()
	{
		if (!_poseCaptured)
		{
			return;
		}

		_poseCaptured = false;
		if (_phone != null)
		{
			_phone.localPosition = _authoredLocalPosition;
			_phone.localRotation = _authoredLocalRotation;
		}
	}

	public void Dispose()
	{
		RestoreAuthoredPose();
		_disposed = true;
	}

	private static Transform FindUniqueDescendant(Transform root, string name)
	{
		Transform match = null;
		foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
		{
			if (candidate == root || candidate.name != name)
			{
				continue;
			}

			if (match != null)
			{
				return null;
			}
			match = candidate;
		}
		return match;
	}

	private static string GetPath(Transform transform)
	{
		string path = transform.name;
		for (Transform parent = transform.parent; parent != null; parent = parent.parent)
		{
			path = parent.name + "/" + path;
		}
		return path;
	}
}
