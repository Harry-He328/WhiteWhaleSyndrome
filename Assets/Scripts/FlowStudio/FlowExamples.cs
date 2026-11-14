using System.IO;
using UnityEngine;

namespace WhiteWhale.FlowStudio
{
    internal static class FlowFiles
    {
        public static void CopyBundledFiles(FlowFileStore files)
        {
            string bundled = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "Flows"));
            if (files.Root == bundled || !Directory.Exists(bundled)) return;
            foreach (string source in Directory.GetFiles(bundled, "*.flow.json"))
            {
                string destination = files.PathFor(Path.GetFileName(source));
                if (!File.Exists(destination)) File.Copy(source, destination);
            }
            files.CopyStartupSettings(bundled);
        }
    }
}
