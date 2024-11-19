using System.Xml.Linq;
using System.Xml.Serialization;

namespace OC.RobotStudio;

internal class IoDevice
{
    public bool HasSubModules => SubModules?.Count() > 0;
    public bool IsHostDevice => DeviceItem?.HostDevice is null;
    public IEnumerable<IoDevice>? SubModules { get; }
    public IEnumerable<IoSignal>? Inputs { get; }
    public IEnumerable<IoSignal>? Outputs { get; }
    public DeviceItem? DeviceItem { get; }
        
    public IoDevice(XNode node)
    {
        var doc = node.Document;

        var serializer = new XmlSerializer(typeof(DeviceItem));
        using (var reader = node.CreateReader())
        {
            if (serializer.CanDeserialize(reader))
            {
                DeviceItem = serializer.Deserialize(reader) as DeviceItem;
            }
            if (DeviceItem is null) throw new Exception("Deserialization error");
        }

        if (DeviceItem.HostDevice is null)
        {
            SubModules = (from module in node.Parent?.Descendants()
                from att in module.Attributes()
                where att.Name == "HostDevice" && att.Value == DeviceItem.Name
                select new IoDevice(module)).ToList();
        }

        if (doc is null) return;
        Inputs = GetInputs(doc);
        Outputs = GetOutputs(doc);
    }

    private IEnumerable<IoSignal> GetInputs(XDocument doc)
    {
        var io = (from eio in doc.Root?.Descendants("EIO_SIGNAL") 
            from item in eio.Descendants("Item") 
            where (item.Attribute("SignalType")?.Value == "DI" || 
                   item.Attribute("SignalType")?.Value == "GI" ||
                   item.Attribute("SignalType")?.Value == "AI") && 
                  item.Attribute("Device")?.Value == DeviceItem?.Name 
            select new IoSignal(item)).ToList().OrderBy(item => item.Index); 
        return io;
    }

    private IEnumerable<IoSignal> GetOutputs(XDocument doc)
    {
        var io = (from eio in doc.Root?.Descendants("EIO_SIGNAL") 
            from item in eio.Descendants("Item") 
            where (item.Attribute("SignalType")?.Value == "DO" ||
                   item.Attribute("SignalType")?.Value == "GO" ||
                   item.Attribute("SignalType")?.Value == "AO") &&
                  item.Attribute("Device")?.Value == DeviceItem?.Name
            select new IoSignal(item)).ToList().OrderBy(item => item.Index);
        return io;
    }
}