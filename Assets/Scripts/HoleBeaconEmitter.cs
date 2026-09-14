using System;
using UnityEngine;

public class HoleBeaconEmitter : MonoBehaviour
{
    [Header("Pulse Timing")]
    [SerializeField] private float pulseRateHz = 10f;

    [Header("Infrared")]
    [SerializeField] private float infraredFrequencyHz = 3300f;
    [SerializeField] private float infraredPulseWidthSeconds = 0.05f;

    [Header("Ultrasound")]
    [SerializeField] private float ultrasoundFrequencyHz = 40000f;
    [SerializeField] private float ultrasoundPulseWidthSeconds = 0.001f;

    public bool InfraredActive { get; private set; }
    public bool UltrasoundActive { get; private set; }
    public ulong PulseIndex { get; private set; }

    private void Update()
    {
        double period = 1.0 / Math.Max(0.01f, pulseRateHz);
        double elapsed = Time.timeAsDouble;

        PulseIndex = (ulong)Math.Floor(elapsed / period);
        double phase = elapsed - PulseIndex * period;

        InfraredActive = phase < infraredPulseWidthSeconds;
        UltrasoundActive = phase < ultrasoundPulseWidthSeconds;
    }
}