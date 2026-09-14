using UnityEngine;

[RequireComponent(typeof(Light))]
public class HoleLightBlink : MonoBehaviour
{
    [SerializeField] private float onTime = 0.5f;
    [SerializeField] private float offTime = 0.5f;

    private Light holeLight;
    private float nextSwitchTime;

    private void Awake()
    {
        holeLight = GetComponent<Light>();
    }

    private void OnEnable()
    {
        holeLight = GetComponent<Light>();
        holeLight.enabled = true;
        nextSwitchTime = Time.time + onTime;
    }

    private void Update()
    {
        if (Time.time < nextSwitchTime)
            return;

        holeLight.enabled = !holeLight.enabled;
        nextSwitchTime = Time.time + (holeLight.enabled ? onTime : offTime);
    }
}