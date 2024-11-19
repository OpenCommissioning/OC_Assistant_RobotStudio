using System.ComponentModel;
using System.Xml.Serialization;

namespace OC.RobotStudio;

[Serializable]
[DesignerCategory("code")]
[XmlType("Item", AnonymousType = true)]
[XmlRoot("Item", Namespace = "", IsNullable = false)]
public class DeviceItem
{
    private string? _nameField;
    private string? _vendorNameField;
    private string? _productNameField;
    private string? _labelField;
    private int _outputSizeField;
    private bool _outputSizeFieldSpecified;
    private int _inputSizeField;
    private bool _inputSizeFieldSpecified;
    private string? _iM1FunctionField;
    private string? _iM2InstallationDateField;
    private string? _hostDeviceField;
    private int _slotIndexField;
    private bool _slotIndexFieldSpecified;
    private int _moduleIdField;
    private bool _moduleIdFieldSpecified;
    private string? _safetyEnabledField;
    private string? _simulatedField;
    private string? _stationNameField;
        
    [XmlAttribute]
    public string? Name
    {
        get => _nameField;
        set => _nameField = value;
    }
        
    [XmlAttribute]
    public string? VendorName
    {
        get => _vendorNameField;
        set => _vendorNameField = value;
    }
        
    [XmlAttribute]
    public string? ProductName
    {
        get => _productNameField;
        set => _productNameField = value;
    }
        
    [XmlAttribute]
    public string? Label
    {
        get => _labelField;
        set => _labelField = value;
    }
        
    [XmlAttribute]
    public int OutputSize
    {
        get => _outputSizeField;
        set => _outputSizeField = value;
    }
        
    [XmlIgnore]
    public bool OutputSizeSpecified
    {
        get => _outputSizeFieldSpecified;
        set => _outputSizeFieldSpecified = value;
    }
        
    [XmlAttribute]
    public int InputSize
    {
        get => _inputSizeField;
        set => _inputSizeField = value;
    }
        
    [XmlIgnore]
    public bool InputSizeSpecified
    {
        get => _inputSizeFieldSpecified;
        set => _inputSizeFieldSpecified = value;
    }
        
    [XmlAttribute]
    public string? Im1Function
    {
        get => _iM1FunctionField;
        set => _iM1FunctionField = value;
    }
        
    [XmlAttribute]
    public string? Im2InstallationDate
    {
        get => _iM2InstallationDateField;
        set => _iM2InstallationDateField = value;
    }
        
    [XmlAttribute]
    public string? HostDevice
    {
        get => _hostDeviceField;
        set => _hostDeviceField = value;
    }
        
    [XmlAttribute]
    public int SlotIndex
    {
        get => _slotIndexField;
        set => _slotIndexField = value;
    }
        
    [XmlIgnore]
    public bool SlotIndexSpecified
    {
        get => _slotIndexFieldSpecified;
        set => _slotIndexFieldSpecified = value;
    }
        
    [XmlAttribute]
    public int ModuleId
    {
        get => _moduleIdField;
        set => _moduleIdField = value;
    }
        
    [XmlIgnore]
    public bool ModuleIdSpecified
    {
        get => _moduleIdFieldSpecified;
        set => _moduleIdFieldSpecified = value;
    }
        
    [XmlAttribute]
    public string? SafetyEnabled
    {
        get => _safetyEnabledField;
        set => _safetyEnabledField = value;
    }
        
    [XmlAttribute]
    public string? Simulated
    {
        get => _simulatedField;
        set => _simulatedField = value;
    }
        
    [XmlAttribute]
    public string? StationName
    {
        get => _stationNameField;
        set => _stationNameField = value;
    }
}