// CREDIT TO AiKodex
// https://assetstore.unity.com/packages/tools/integration/simple-spin-blur-202273
using System.Collections.Generic;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public class SimpleSpinBlur : UpdatableComponentBase
{
    private Mesh _ssbMesh;
    private Material _ssbMaterial;
    private readonly Queue<Quaternion> _rotationQueue = new();

    [Range(1, 128)]
    [Tooltip("Motion Blur Amount")]
    public int shutterSpeed = 4;

    [Range(1, 50)]
    [Tooltip("Motion Blur Samples")]
    public int samples = 8;

    [Range(-0.1f, 0.1f)]
    [Tooltip("Motion Blur Opacity")]
    public float alphaOffset;

    [Tooltip("[Optimization] Enables material's GPU Instancing property")]
    public bool enableGPUInstancing;

    [Tooltip("[Optimization] Angular velocity threshold value before which the effects will not be rendered.")]
    public float angularVelocityCutoff;

    public override void ManualUpdate()
    {
        if (_ssbMesh == null || _ssbMaterial == null || transform == null)
        {
            return;
        }

        int safeShutterSpeed = Mathf.Max(1, shutterSpeed);
        int safeSamples = Mathf.Max(1, samples);

        while (_rotationQueue.Count >= safeShutterSpeed)
        {
            _rotationQueue.Dequeue();
        }

        _rotationQueue.Enqueue(transform.rotation);
        Quaternion oldestRotation = _rotationQueue.Peek();
        float angularVelocity = Quaternion.Angle(transform.rotation, oldestRotation) / safeShutterSpeed;

        if (angularVelocity >= angularVelocityCutoff)
        {
            for (int i = 0; i <= safeSamples; i++)
            {
                Graphics.DrawMesh(
                    _ssbMesh,
                    transform.position,
                    Quaternion.Lerp(oldestRotation, transform.rotation, i / (float)safeSamples),
                    _ssbMaterial,
                    0,
                    null,
                    0);
            }

            Color color = SafeGetColor(_ssbMaterial, Color.white);
            color.a = Mathf.Abs((2 / (float)safeSamples) + alphaOffset);
            SafeSetColor(_ssbMaterial, color);
            return;
        }

        Color currentColor = SafeGetColor(_ssbMaterial, Color.white);
        if (currentColor.a >= 1f)
        {
            return;
        }

        currentColor.a = 1f;
        SafeSetColor(_ssbMaterial, currentColor);
    }

    protected override void OnStart()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        Renderer renderer = GetComponent<Renderer>();

        _ssbMesh = meshFilter != null ? meshFilter.mesh : null;
        _ssbMaterial = renderer != null ? renderer.sharedMaterial : null;

        if (_ssbMaterial != null)
        {
            _ssbMaterial.enableInstancing = enableGPUInstancing;
        }

        HasFinishedInitialization = true;
    }

    private static Color SafeGetColor(Material material, Color fallback)
    {
        if (material == null || !material.HasProperty("_Color"))
        {
            return fallback;
        }

        try
        {
            return material.color;
        }
        catch
        {
            return fallback;
        }
    }

    private static void SafeSetColor(Material material, Color color)
    {
        if (material == null || !material.HasProperty("_Color"))
        {
            return;
        }

        try
        {
            material.color = color;
        }
        catch
        {
            // Material may be invalid during raid teardown. Ignore instead of spamming Update exceptions.
        }
    }
}
