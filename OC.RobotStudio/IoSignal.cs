using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;
using ABB.Robotics.Controllers;
using ABB.Robotics.Controllers.IOSystemDomain;
using OC.Assistant.Sdk;

namespace OC.RobotStudio;

internal partial class IoSignal
{
    private const float TOLERANCE = 1.0E-9f;
    private Signal? _signal;
    private readonly int _index;
    private float _value;
    public IoItem? IoItem { get; }
    public int Index => _index;
    public int Length { get; }

    /// <summary>
    /// Bit offset of this signal in the TwinCAT exchange buffer, including reserved padding.
    /// Process I/O is 0-based. Safety I/O is offset to the start of that region.
    /// </summary>
    public int BufferBit { get; set; }

    /// <summary>
    /// DeviceMap origin of this signal's unit image. 0 for process I/O, 2048 for safety.
    /// </summary>
    public int ImageBitBase { get; set; }

    public string? UnitName { get; private set; }

    public bool IsValid => _signal is not null;
        
    public IoSignal(XNode signal)
    {
        var serializer = new XmlSerializer(typeof(IoItem));
        using (var reader = signal.CreateReader())
        {
            if (serializer.CanDeserialize(reader))
            {
                IoItem = serializer.Deserialize(reader) as IoItem;
            }

            if (IoItem is null)
            {
                Logger.LogError(this, "Deserialization error");
                return;
            }
        }

        if (IoItem.DeviceMap is null) return;
        var ranges = AddressRangeRegex().Matches(IoItem.DeviceMap);
        if (ranges.Count > 0)
        {
            var length = 0;
            var index = int.MaxValue;
            foreach (Match range in ranges)
            {
                var low = int.Parse(range.Groups["LOW"].Value);
                var high = int.Parse(range.Groups["HIGH"].Value);
                length += high - low + 1;
                if (low < index) index = low;
            }

            Length = length;
            _index = index;
        }
        else
        {
            int.TryParse(IoItem.DeviceMap, out _index);
            Length = 1;
        }
    }
    
    public bool ValueAsBool => Math.Abs(_value) > 0.5f;
    public uint ValueAsUInt32Bits => _signal?.Type is SignalType.GroupOutput or SignalType.GroupInput or SignalType.AnalogOutput
        ? (uint)Math.Round(Math.Max(_value, 0f))
        : BitConverter.SingleToUInt32Bits(_value);

    public float Value
    {
        get => _value;
        set
        {
            if (Math.Abs(value - _value) < TOLERANCE) return;
            _value = value;
            try
            {
                if (_signal is null) return;
                _signal.Value = value;
            }
            catch (Exception e)
            {
                Logger.LogError(this, e.Message);
            }
        }
    }

    /// <summary>
    /// Bit width of the TwinCAT BYTE/WORD/DWORD that holds this signal.
    /// </summary>
    public int ContainerBits => Length switch
    {
        <= 1 => 1,
        <= 8 => 8,
        <= 16 => 16,
        _ => 32
    };

    /// <summary>
    /// Applies one unit output image. Does not write back to the robot.
    /// Process DOs stay on the device image. Groups and safety bits use the
    /// controller value so every GO/FGO/GI/FGI/FDO matches the pendant name.
    /// </summary>
    public void ApplyOutputImage(byte[] data, byte[] mask)
    {
        if (Length <= 0) return;

        if (Length > 1 || ImageBitBase > 0)
        {
            RefreshControllerValue();
            return;
        }

        var map = Index;
        var imageBits = data.Length * 8;
        if (map < 0 || map >= imageBits) return;

        var byteIndex = map / 8;
        var bitIndex = map % 8;
        if ((uint)byteIndex >= (uint)mask.Length) return;
        if (((mask[byteIndex] >> bitIndex) & 1) == 0) return;
        _value = ((data[byteIndex] >> bitIndex) & 1) != 0 ? 1f : 0f;
    }

    /// <summary>
    /// Pendant value for groups and safety bits. No byte-swap — layout is fixed in the buffer write.
    /// </summary>
    public void RefreshControllerValue()
    {
        if (_signal is null) return;
        try
        {
            _value = _signal.Value;
        }
        catch (Exception e)
        {
            Logger.LogError(this, e.Message);
        }
    }

    public void Map(Signal signal)
    {
        
        _signal = signal;
        UnitName = _signal.Unit;
        switch (_signal.Type)
        {
            case SignalType.AnalogOutput:
            case SignalType.DigitalOutput:
            case SignalType.GroupOutput:
                _value = _signal.Value;
                break;
            case SignalType.DigitalInput:
            case SignalType.GroupInput:
            case SignalType.AnalogInput:
                //_signal.Changed += SignalOnChanged;
                _signal.InputAsPhysical = true;
                _signal.Value = 0f;
                break;
            case SignalType.Unknown:
            default:
                Logger.LogWarning(this, $"Signal type '{_signal.Type}' for signal '{_signal.Name}' not supported");
                break;
        }
    }

    private void SignalOnChanged(object? sender, SignalChangedEventArgs e)
    {
        try
        {
            _value = e.NewSignalState.Value;
            Logger.LogInfo(this, $"Signal '{_signal?.Name}' changed value to {_value}", true);
        }
        catch (Exception ex)
        {
            Logger.LogError(this, $"error: {ex.Message}");
        }
    }

    [GeneratedRegex(@"(?<LOW>\d+)-(?<HIGH>\d+)")]
    private static partial Regex AddressRangeRegex();
}