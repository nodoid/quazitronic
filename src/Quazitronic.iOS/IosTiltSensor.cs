using Microsoft.Xna.Framework;
using CoreMotion;
using Quazitronic.Input;

namespace Quazitronic.iOS;

/// <summary>Device gravity from CoreMotion (already in the convention <see cref="ITiltSensor"/> expects).</summary>
internal sealed class IosTiltSensor : ITiltSensor
{
    private readonly CMMotionManager _motion = new();

    public bool IsAvailable => _motion.DeviceMotionAvailable;

    public void Start()
    {
        if (!IsAvailable || _motion.DeviceMotionActive) return;
        _motion.DeviceMotionUpdateInterval = 1.0 / 60.0;
        _motion.StartDeviceMotionUpdates();
    }

    public void Stop()
    {
        if (_motion.DeviceMotionActive) _motion.StopDeviceMotionUpdates();
    }

    public bool TryRead(out Vector3 gravity)
    {
        var g = _motion.DeviceMotion?.Gravity;
        if (g == null)
        {
            gravity = default;
            return false;
        }
        gravity = new Vector3((float)g.Value.X, (float)g.Value.Y, (float)g.Value.Z);
        return true;
    }
}
