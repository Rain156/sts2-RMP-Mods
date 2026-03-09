using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemoveMultiplayerPlayerLimit.src
{
    public static class Pathes
    {
        public static string RootPath = Path.GetDirectoryName(OS.GetExecutablePath());

        public static string ModsPath = Path.Combine(RootPath, "mods");

        public static string ProjectPath = Path.Combine(ModsPath, ModEntry.ModFolderName);

        public static string ConfigPath = Path.Combine(ProjectPath, ModEntry.ConfigFileName);
    }
}
