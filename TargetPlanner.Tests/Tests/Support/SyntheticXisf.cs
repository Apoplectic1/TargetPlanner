using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TargetPlanner.Tests.Tests.Support
{
    // Builds a minimal valid XISF file with the given FITS keywords. Adapted from
    // Library\Astronomy.XISF.Tests\XisfHeaderReaderTests.cs WriteSyntheticXisf
    // helper -- cross-repo duplication, sync if either drifts. (It drifted once:
    // AL made the <Image> `geometry` attribute mandatory on 2026-07-29 — it is
    // mandatory per the XISF spec — and this copy lagged until 2026-08-01, failing
    // six tests.) Header-only (no image attachment block) is enough for any caller
    // that uses XisfHeaderReader: 8-byte ASCII signature + 4-byte LE XML length +
    // 4-byte reserved + UTF-8 XML payload.
    //
    // RA / DEC values written here are FITS-standard degrees -- ImageLibraryLoader
    // divides RA by 15 to produce decimal hours.
    public static class SyntheticXisf
    {
        public static void Write(string path, IDictionary<string, string> fitsKeywords)
        {
            StringBuilder xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            xml.Append("<xisf version=\"1.0\" xmlns=\"http://www.pixinsight.com/xisf\">");
            xml.Append("<Image geometry=\"5496:3672:1\">");
            foreach (KeyValuePair<string, string> kv in fitsKeywords)
            {
                // Strings get FITS-quoted; numerics unquoted.
                string val = double.TryParse(kv.Value, out _) ? kv.Value : $"'{kv.Value}'";
                xml.Append($"<FITSKeyword name=\"{kv.Key}\" value=\"{val}\" comment=\"\" />");
            }
            xml.Append("</Image>");
            xml.Append("</xisf>");

            byte[] xmlBytes = Encoding.UTF8.GetBytes(xml.ToString());

            byte[] header = new byte[16];
            Encoding.ASCII.GetBytes("XISF0100", 0, 8, header, 0);
            int len = xmlBytes.Length;
            header[8] = (byte)(len & 0xFF);
            header[9] = (byte)((len >> 8) & 0xFF);
            header[10] = (byte)((len >> 16) & 0xFF);
            header[11] = (byte)((len >> 24) & 0xFF);
            // bytes 12-15 reserved (left as zero)

            using FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            fs.Write(header, 0, 16);
            fs.Write(xmlBytes, 0, xmlBytes.Length);
        }

        public static IDictionary<string, string> LightFrameKeywords(
            string objectName, double raDeg, double decDeg) =>
            new Dictionary<string, string>
            {
                ["OBJECT"]   = objectName,
                ["RA"]       = raDeg.ToString("R"),
                ["DEC"]      = decDeg.ToString("R"),
                ["IMAGETYP"] = "LIGHT",
            };
    }
}
