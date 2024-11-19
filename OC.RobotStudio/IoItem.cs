using System.ComponentModel;
using System.Xml.Serialization;

namespace OC.RobotStudio;

[Serializable]
[DesignerCategory("code")]
[XmlType("Item", AnonymousType = true)]
[XmlRoot("Item", Namespace = "", IsNullable = false)]
public class IoItem
{
    private string? _nameField;
    private string? _signalTypeField;
    private string? _deviceField;
    private string? _labelField;
    private string? _deviceMapField;
    private string? _categoryField;
    private string? _accessField;
    private string _encType = string.Empty;
        
    [XmlAttribute]
    public string? Name
    {
        get => _nameField;
        set => _nameField = value;
    }
        
    [XmlAttribute]
    public string? SignalType
    {
        get => _signalTypeField;
        set => _signalTypeField = value;
    }
        
    [XmlAttribute]
    public string? Device
    {
        get => _deviceField;
        set => _deviceField = value;
    }
        
    [XmlAttribute]
    public string? Label
    {
        get => _labelField;
        set => _labelField = value;
    }
        
    [XmlAttribute]
    public string? DeviceMap
    {
        get => _deviceMapField;
        set => _deviceMapField = value;
    }
        
    [XmlAttribute]
    public string? Category
    {
        get => _categoryField;
        set => _categoryField = value;
    }
        
    [XmlAttribute]
    public string? Access
    {
        get => _accessField;
        set => _accessField = value;
    }
    [XmlAttribute]
    public int MaxBitVal
    {
        get => _maxBitValue;
        set => _maxBitValue = value;
    }
    private int _maxBitValue;

    [XmlAttribute]
    public int MinBitVal
    {
        get => _minBitValue;
        set => _minBitValue = value;
    }
    private int _minBitValue ;

    [XmlAttribute]
    public int MinLog
    {
        get => _minLog;
        set => _minLog = value;
    }
    private int _minLog;

    [XmlAttribute]
    public int MaxLog
    {
        get => _maxLog;
        set => _maxLog = value;
    }
    private int _maxLog;

    [XmlAttribute]
    public int MinPhys
    {
        get => _minPhys;
        set => _minPhys = value;
    }
    private int _minPhys;

    [XmlAttribute]
    public int MaxPhys
    {
        get => _maxPhys;
        set => _maxPhys = value;
    }
    private int _maxPhys;

    [XmlAttribute]
    public int MaxPhysLimit
    {
        get => _maxPhysLimit;
        set => _maxPhysLimit = value;
    }
    private int _maxPhysLimit;
        
    [XmlAttribute]
    public string EncType
    {
        get => _encType;
        set => _encType = value;
    }
}