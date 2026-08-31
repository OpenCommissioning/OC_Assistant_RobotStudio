using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OC.Assistant.Sdk;

namespace OC.RobotStudio;

/// <summary>
/// Static class to parse a RobotStudio EIO.cfg file.
/// </summary>
internal static partial class CfgParser
{
    private static readonly string[] DeviceTypes =
    [
        "PROFINET_INTERNAL_DEVICE", 
        "PROFINET_DEVICE", 
        "DEVICENET_DEVICE", 
        "ETHERNETIP_DEVICE"
    ];

    /// <summary>
    /// Parses a file to XML structure.
    /// </summary>
    public static XDocument? ConvertToXml(string fileName)
    {
        var xDoc = new XDocument();
        try
        {
            if (!File.Exists(fileName))
            {
                Logger.LogError(typeof(CfgParser), $"File \"{fileName}\" not found!");
                return null;
            }

            //Read file and split into sections
            var sections = SectionRegex().Matches(File.ReadAllText(fileName, Encoding.UTF8));

            //Parse all sections and create a xml structure
            foreach (Match section in sections)
            {
                if (xDoc.Root is null)
                {
                    var header = CreateRootElement(section);
                    if (header is null) continue;
                    xDoc.Add(header);
                    var fi = new FileInfo(fileName);
                    xDoc.Root?.Add(new XAttribute("FileName", fi.FullName));
                }
                else
                {
                    xDoc.Root.Add(ParseSection(section));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(typeof(CfgParser), ex.Message);
        }
        return xDoc;
    }
        
    public static IEnumerable<IoDevice> GetAllDevices(this XDocument eioXml)
    {
        var devices = new List<IoDevice>();
        foreach (var device in DeviceTypes)
        {
            try
            {
                // get all devices
                var dev = (from node in eioXml.Root?.Descendants(device) 
                    from item in node.Descendants("Item")
                    select new IoDevice(item)).ToList();
                // host-devices only!
                dev = (from item in dev 
                    where item.IsHostDevice
                    select item).ToList();
                devices = devices.Concat(dev).ToList();
            }
            catch (Exception e)
            {                    
                Logger.LogError(typeof(CfgParser), e.Message);
            }
        }
        return devices.ToArray();
    }

    /// <summary>
    /// Gets the header of a section and parses the value.
    /// </summary>
    private static XElement? ParseSection(Capture section)
    {
        var matchHeader = HeaderRegex().Match(section.Value);

        if (matchHeader.Success)
        {
            var xElemSection = new XElement(matchHeader.Value);
            foreach (var item in GetValues(section))
            {
                xElemSection.Add(item);
            }
            return xElemSection;
        }
        Logger.LogError(typeof(CfgParser), $"Parsing of section '{matchHeader.Value}' failed");
        return null;
    }

    /// <summary>
    /// Gets a list of XElement with parameter values of a section.
    /// </summary>
    private static List<XElement> GetValues(Capture section)
    {
        var values = new List<XElement>();
        
        foreach (var dataSet in GetDataSets(section))
        {
            var xValue = new XElement("Item");
            var matches = ValueRegex().Matches(dataSet);
            foreach (Match m in matches)
            {
                xValue.Add(new XAttribute(m.Groups["NAME"].Value, m.Groups["VALUE"].Value.Trim("\"".ToCharArray())));
            }

            values.Add(xValue);
        }
        return values;
    }

    /// <summary>
    /// Groups all lines of a section to a dataset.<br/>
    /// Multiline datasets with a '\' will be combined.
    /// </summary>
    private static List<string> GetDataSets(Capture section)
    {
        var sep = new[] { "\n", "\r\n" };
        var rawLines = section.Value.Split(sep, StringSplitOptions.RemoveEmptyEntries);

        var dataSets = new List<string>();
        var sbTmpLines = new StringBuilder(rawLines.Length);
        foreach (var rawLine in rawLines)
        {
            var line = rawLine.Trim();
            if (line.EndsWith('\\'))
            {
                sbTmpLines.Append(line.TrimEnd("\\".ToCharArray()).Trim());
                sbTmpLines.Append(' ');
            }
            else
            {
                if (!line.StartsWith('-')) continue;
                if (sbTmpLines.Length > 0)
                {
                    sbTmpLines.Append(line);
                    dataSets.Add(sbTmpLines.ToString());
                    sbTmpLines.Clear();
                }
                else
                {
                    dataSets.Add(line);
                }
            }
        }

        return dataSets;
    }
    
    private static XElement? CreateRootElement(Capture section)
    {
        var match = RootRegex().Match(section.Value);
        if (match.Success)
        {
            return new XElement(match.Groups["NAME"].Value, 
                new XAttribute("Version", $"{match.Groups["MAJOR"].Value}.{match.Groups["MINOR"].Value}"));
        }
        return null;
    }
    
    [GeneratedRegex(@"^(?<NAME>\w+):.*?:(?<MAJOR>\d+):(?<MINOR>\d+)::")]
    private static partial Regex RootRegex();
    
    [GeneratedRegex(@"^(?<HEADER>\w+)(?=:)")]
    private static partial Regex HeaderRegex();
    
    [GeneratedRegex("""
                    ^\w+:(?>^\w+:(?<DEPTH>)|(#(\s+)?
                    |\Z)(?<-DEPTH>)|(?!(^\w+:|(#(\s+)?
                    |\Z)))(\w+|\W))*(#(\s+)?
                    |\Z)(?(DEPTH)(?!))
                    """, RegexOptions.Multiline)]
    private static partial Regex SectionRegex();
    
    [GeneratedRegex("""(?<NAME>(?!-)\w+)(?(?=\Z)\Z|\s+(?(?=\s|\Z)(\s|\Z)|(?(?=-?\d)(?<VALUE>-?\d+([\.,]\d+(E[+-]\d+)?)?)|"(?<VALUE>.+?)")))""")]
    private static partial Regex ValueRegex();
}