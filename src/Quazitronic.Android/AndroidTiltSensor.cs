using Microsoft.Xna.Framework;
using Android.Content;
using Android.Hardware;
using Quazitronic.Input;

namespace Quazitronic.Android;

/// <summary>
/// Device gravity from the Android gravity sensor (or a low-pass filtered accelerometer).
/// Android reports the reaction to gravity in m/s², so it is negated and scaled to g to match
/// the CoreMotion convention <see cref="ITiltSensor"/> uses.
/// </summary>
internal sealed class AndroidTiltSensor : Java.Lang.Object, ITiltSensor, ISensorEventListener
{
    private const float Smoothing = 0.25f;

    private readonly SensorManager? _manager;
    private readonly Sensor? _sensor;
    private readonly bool _isRawAccelerometer;
    private readonly object _lock = new();
    private Vector3 _gravity;
    private bool _hasReading;
    private bool _running;

    public AndroidTiltSensor(Context context)
    {
        _manager = context.GetSystemService(Context.SensorService) as SensorManager;
        _sensor = _manager?.GetDefaultSensor(SensorType.Gravity);
        if (_sensor == null)
        {
            _sensor = _manager?.GetDefaultSensor(SensorType.Accelerometer);
            _isRawAccelerometer = _sensor != null;
        }
    }

    public bool IsAvailable => _sensor != null;

    public void Start()
    {
        if (_running || _manager == null || _sensor == null) return;
        _running = _manager.RegisterListener(this, _sensor, SensorDelay.Game);
    }

    public void Stop()
    {
        if (!_running) return;
        _manager?.UnregisterListener(this);
        _running = false;
        lock (_lock) _hasReading = false;
    }

    public bool TryRead(out Vector3 gravity)
    {
        lock (_lock)
        {
            gravity = _gravity;
            return _hasReading;
        }
    }

    public void OnSensorChanged(SensorEvent? e)
    {
        if (e?.Values is not { Count: >= 3 } v) return;
        var g = new Vector3(-v[0], -v[1], -v[2]) / SensorManager.StandardGravity;
        lock (_lock)
        {
            // The raw accelerometer also picks up hand shake; smooth it. The gravity sensor is already fused.
            _gravity = _hasReading && _isRawAccelerometer ? Vector3.Lerp(_gravity, g, Smoothing) : g;
            _hasReading = true;
        }
    }

    public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy)
    {
    }
}
