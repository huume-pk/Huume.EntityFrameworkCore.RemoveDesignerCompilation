using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Huume.EntityFrameworkCore.RemoveDesignerCompilation
{
    internal class EFCoreFileManipulator
    {
        static readonly Regex MigrationRegex = new Regex(@".*\[Migration\(""(?<MigrationName>.*)""\)\].*(\r\n|\r|\n)", RegexOptions.Compiled);
        static readonly Regex DbContextRegex = new Regex(@".*\[DbContext\(typeof\((?<DBContextName>.*)\)\)\].*(\r\n|\r|\n)", RegexOptions.Compiled);
        static readonly Regex LineBreakDetection = new Regex(@".*(?<LineBreakStyle>(\r\n|\r|\n))", RegexOptions.Compiled);
        
        private readonly TaskLoggingHelper _taskLoggingHelper;

        public EFCoreFileManipulator(TaskLoggingHelper taskLoggingHelper)
        {
            _taskLoggingHelper = taskLoggingHelper;
        }

        public (List<string> excludedFiles, List<string> keptFiles) ProcessDirectory(DirectoryInfo directory, string namespaceName, int designerFileCountToKeep, string ignoreFilename)
        {
            var excludeFiles = new List<string>();
            var keptFiles = new List<string>();

            var filesToProcess = new List<(FileInfo MigrationFile, FileInfo DesignerFile)>();
            var keepCount = 0;

            foreach (var file in directory.EnumerateFiles("*.Designer.cs").OrderByDescending(x => x.Name))
            {
                if (!string.IsNullOrEmpty(ignoreFilename) && file.Name.Contains(ignoreFilename))
                {
                    keptFiles.Add(file.FullName);
                    continue;
                }
                
                if (designerFileCountToKeep > 0 && keepCount < designerFileCountToKeep)
                {
                    keepCount++;
                    keptFiles.Add(file.FullName);
                    continue;
                }

                filesToProcess.Add((new FileInfo(file.FullName.Replace(".Designer.cs", ".cs")), file));
            }

            foreach (var (migrationFile, designerFile) in filesToProcess.OrderBy(x => x.MigrationFile.Name))
            {
                if (!designerFile.Exists || !migrationFile.Exists)
                {
                    _taskLoggingHelper.LogMessage(MessageImportance.High, $"RDC: File {migrationFile} or {designerFile} does not exist.");
                }

                if (ProcessFiles(migrationFile, designerFile, namespaceName))
                {
                    excludeFiles.Add(designerFile.FullName);
                }
            }

            return (excludeFiles, keptFiles);
        }

        private bool ProcessFiles(FileInfo migrationFile, FileInfo designerFile, string namespaceName)
        {
            var designerFileContent = File.ReadAllText(designerFile.FullName);
            var codeFileContent = File.ReadAllText(migrationFile.FullName);
            var lineEndingStyle = GetLineEndingStyle(codeFileContent);

            if (!designerFileContent.Contains("[Migration(")
                || !designerFileContent.Contains("[DbContext(typeof(")
                || !DbContextRegex.IsMatch(designerFileContent)
                || !MigrationRegex.IsMatch(designerFileContent))
            {
                _taskLoggingHelper.LogMessage(MessageImportance.High, $"RDC: File {designerFile} is not a migration that we need to process (it was probably already processed).");
                return true;
            }

            if (codeFileContent.Contains("[Migration(")
                || codeFileContent.Contains("[DbContext(typeof(")
                || DbContextRegex.IsMatch(codeFileContent)
                || MigrationRegex.IsMatch(codeFileContent))
            {
                _taskLoggingHelper.LogMessage(MessageImportance.High, $"RDC: File {migrationFile} already has the necessary attributes. (it was probably already processed).");
                return true;
            }

            var migrationMatchValue = MigrationRegex.Match(designerFileContent).Value;
            var dbContextMatchValue = DbContextRegex.Match(designerFileContent).Value;

            _taskLoggingHelper.LogMessage(MessageImportance.High, $"Processing {designerFile}.");
            var indexOfPartialClass = codeFileContent.IndexOf("public partial class", StringComparison.Ordinal);

            if (indexOfPartialClass == -1)
            {
                _taskLoggingHelper.LogWarning("RDC: Could not find public partial class declaration. Ignoring file");
                return false;
            }

            var lastNewline = codeFileContent.LastIndexOf('\n', indexOfPartialClass, indexOfPartialClass);
            codeFileContent = codeFileContent.Insert(lastNewline + 1, migrationMatchValue);
            codeFileContent = codeFileContent.Insert(lastNewline + 1, dbContextMatchValue);

            designerFileContent = designerFileContent.Replace(dbContextMatchValue, string.Empty);
            designerFileContent = designerFileContent.Replace(migrationMatchValue, string.Empty);

            codeFileContent = AddUsingIfNecessary(codeFileContent, namespaceName, lineEndingStyle);

            File.WriteAllText(designerFile.FullName, designerFileContent);
            File.WriteAllText(migrationFile.FullName, codeFileContent);

            return true;
        }

        private string AddUsingIfNecessary(string codeContent, string namespaceName, string lineEndingStyle)
        {
            if (!codeContent.Contains("using Microsoft.EntityFrameworkCore.Infrastructure;"))
            {
                codeContent = codeContent.Insert(0, $"using Microsoft.EntityFrameworkCore.Infrastructure;{lineEndingStyle}");
            }

            if (!codeContent.Contains($"using {namespaceName};"))
            {
                codeContent = codeContent.Insert(0, $"using {namespaceName};{lineEndingStyle}");
            }

            return codeContent;
        }

        private string GetLineEndingStyle(string content)
        {
            var match = LineBreakDetection.Match(content);
            return match.Groups["LineBreakStyle"].Value;
        }
    }
}