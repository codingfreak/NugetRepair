namespace codingfreaks.NugetRepair.Ui.ConsoleApp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Xml.Linq;

    using cfUtils.Logic.Base.Structures;
    using cfUtils.Logic.Base.Utilities;

    internal class Program
    {
        #region methods

        /// <summary>
        /// Is used
        /// </summary>
        /// <returns></returns>
        private static List<CommandlineArgumentInfo> GetArguments()
        {
            return new List<CommandlineArgumentInfo>
            {
                new CommandlineArgumentInfo
                {
                    Abbreviation = "f",
                    ArgumentName = "folder",
                    Description = "The folder where the solution(s) reside in.",
                    IsMandatory = true,
                    OrderPosition = 1,
                    SampleValue = "c:\\mysolution"
                },
                new CommandlineArgumentInfo
                {
                    Abbreviation = "s",
                    ArgumentName = "simulate",
                    Description = "If set only a simulation will be performed and no files will be touched.",
                    IsFlag = true
                },
                new CommandlineArgumentInfo
                {
                    Abbreviation = "l",
                    ArgumentName = "logo",
                    Description = "If set, the app-header will be printed out.",
                    IsFlag = true
                }
            };
        }

        /// <summary>
        /// Recursively retrieves all files inside a given <paramref name="startDir" />.
        /// </summary>
        /// <param name="startDir">The starting directory path.</param>
        /// <returns>All files in the dir and all sub dirs.</returns>
        private static List<FileInfo> GetFiles(string startDir)
        {            
            var dir = new DirectoryInfo(startDir);
            var result = new List<FileInfo>(dir.GetFiles());
            foreach (var subDir in dir.GetDirectories())
            {
                result.AddRange(GetFiles(subDir.FullName));
            }
            return result;
        }

        /// <summary>
        /// Inspects and repairs NuGet- and solution-files if any.
        /// </summary>
        /// <param name="files">The list of files to traverse.</param>
        /// <param name="performUpdate"><c>true</c>, if repair should be performed.</param>
        private static void InspectNugetFiles(List<FileInfo> files, bool performUpdate = false)
        {
            Console.Write("Searching for unnecessary NuGet files...");
            var nuget = files.Where(file => file.Name.ToLower().Contains("nuget.")).ToList();
            ConsoleUtil.Write($"{nuget.Count}", ConsoleColor.DarkMagenta);
            Console.WriteLine(" found.");            
            if (!performUpdate)
            {
                return;
            }            
            var solutions = files.Where(file => file.Name.ToLower().EndsWith(".sln")).ToList();
            Console.Write("Deleting files...");
            nuget.ForEach(nug => nug.Delete());
            ConsoleUtil.WriteLine($"Done. ({ nuget.Count} files deleted.", ConsoleColor.Green);
            Console.Write("Deleting folders...");
            var deletedDirs = new List<string>();
            nuget.ForEach(
                nug =>
                {
                    if (nug.Directory == null || !nug.Directory.Exists || nug.Directory.GetFiles().Any())
                    {
                        return;
                    }
                    nug.Directory.Delete();
                    deletedDirs.Add(nug.Directory.Name.ToLower());
                });
            ConsoleUtil.WriteLine($"Done. ({deletedDirs.Count} folders deleted.", ConsoleColor.Green);            
            Console.Write("Checking solution files...");
            solutions.ForEach(
                sln =>
                {
                    var content = File.ReadAllLines(sln.FullName).ToList();
                    var result = new StringBuilder();
                    var add = true;
                    var resetAdd = false;
                    foreach (var line in content)
                    {
                        if (add)
                        {
                            foreach (var delDir in deletedDirs)
                            {
                                if (line.StartsWith("Project") && line.Contains(delDir))
                                {
                                    // do not add this and coming lines till a line with EndProject occurs
                                    add = false;
                                }
                            }
                        }
                        else
                        {
                            if (line.StartsWith("EndProject"))
                            {
                                resetAdd = true;
                            }
                        }
                        if (add)
                        {
                            // current line is ok so keep it in result
                            result.AppendLine(line);
                        }
                        if (!resetAdd)
                        {
                            // current line should be ignored in result
                            continue;
                        }
                        // resetting add to true which meens the next occuring line should be treated normally again
                        add = true;
                        resetAdd = false;
                    }
                    File.WriteAllText(sln.FullName, result.ToString());
                });
            ConsoleUtil.WriteLine($"Done. ({solutions.Count} solution files deleted.", ConsoleColor.Green);
        }

        /// <summary>
        /// Inspects and repairs project files.
        /// </summary>
        /// <param name="files">The list of files to traverse.</param>
        /// <param name="performUpdate"><c>true</c>, if repair should be performed.</param>
        private static void InspectProjectFiles(List<FileInfo> files, bool performUpdate = false)
        {
            Console.Write("Searching for project files...");
            var project = files.Where(file => file.Name.ToLower().EndsWith("proj")).ToList();
            ConsoleUtil.Write($"{project.Count}", ConsoleColor.DarkMagenta);
            Console.WriteLine(" found.");
            project.ForEach(
                nug =>
                {
                    Console.Write("Inspecting {0}...", nug.FullName);
                    var doc = XDocument.Load(nug.FullName);
                    var node = doc.FirstNode.Document;
                    if (node == null)
                    {
                        return;
                    }
                    //var test = node.Descendants().Where(d => d.Name.LocalName == "Target" && d.Attributes().Any(att => att.Name.LocalName == "Name"));
                    var elements =
                        node.Descendants()
                            .Where(
                                nod =>
                                    (nod.Name.LocalName == "Import" && nod.Attributes().Any(att => att.Name.LocalName == "Project" && att.Value.ToLower().Contains("nuget.targets")))
                                    || (nod.Name.LocalName == "Target" && nod.Attributes().Any(att => att.Name.LocalName == "Name" && att.Value.ToLower().Contains("nuget"))))
                            .ToList();
                    var result = elements.Any() ? "invalid" : "valid";
                    var color = elements.Any() ? ConsoleColor.DarkMagenta : ConsoleColor.Green;
                    ConsoleUtil.WriteLine($"{result}.", color);
                    if (!performUpdate)
                    {
                        return;
                    }
                    // correct the error
                    foreach (var delNode in elements)
                    {
                        delNode.Remove();
                    }
                    doc.Save(nug.FullName);
                });
        }

        private static void Main(string[] args)
        {
            if (string.Join(" ", args).Contains("logo"))
            {
                ConsoleUtil.ShowAppHeader(Assembly.GetExecutingAssembly());
            }
            var appInfo = new ApplicationInfo
            {
                AssemblyInfo = Assembly.GetExecutingAssembly(),
                CommandlineArgumentInfos = GetArguments(),
                ParameterDelimiter = '=',
                ParameterPraefix = '-'
            };
            if (!AppUtil.AreCommandArgumentsValid(args, appInfo))
            {
                ConsoleUtil.PrintArgumentError(appInfo, string.Join("\n", AppUtil.CheckCommandArguments(args, appInfo).AsEnumerable()));
                WaitForUserInput();
                return;
            }
            var list = AppUtil.MapCommandArguments(args, appInfo);
            var dirName = list.First(a => a.Abbreviation == "f").GivenValue;
            var simulate = false;
            if (list.Any(a => a.Abbreviation == "s"))
            {
                simulate = bool.Parse(list.First(a => a.Abbreviation == "s").GivenValue);
            }
            if (simulate)
            {
                ConsoleUtil.WriteLine("!!! SIMULATION MODE !!!", ConsoleColor.Green);                
            }
            else
            {
                // get user feedback for security reasons

            }
            if (!Directory.Exists(dirName))
            {
                Console.WriteLine($"Directory {dirName} not found.");
                WaitForUserInput();
                return;
            }
            Console.Write("Directory: ");
            ConsoleUtil.WriteLine(new DirectoryInfo(dirName).FullName, ConsoleColor.DarkGreen);
            Console.Write("Scanning files...");
            var files = GetFiles(dirName);
            ConsoleUtil.Write($"{files.Count}", ConsoleColor.Green);
            Console.WriteLine(" found.");
            try
            {
                InspectNugetFiles(files, !simulate);
                InspectProjectFiles(files, !simulate);
            }
            catch (Exception ex)
            {
                ConsoleUtil.WriteLine($"Error occured during operation: {ex.Message}.", ConsoleColor.Red);
            }
            if (simulate)
            {
                ConsoleUtil.WriteLine("SIM-MODE: NO CHANGES WHERE MADE TO ANY OF THE ELEMENTS!", ConsoleColor.Green);
            }
            WaitForUserInput();
        }

        /// <summary>
        /// Waits for user input if app is in DEBUG mode.
        /// </summary>
        private static void WaitForUserInput()
        {
#if DEBUG
            ConsoleUtil.WriteLine("\nPress a key to quit the application.", ConsoleColor.Cyan);
            Console.ReadKey();
#else
            ConsoleUtil.WriteLine("\nFinished.", ConsoleColor.Cyan);
#endif
        }

        #endregion
    }
}