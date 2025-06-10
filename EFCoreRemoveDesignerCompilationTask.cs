using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;


namespace Huume.EntityFrameworkCore.RemoveDesignerCompilation
{
    public class EFCoreRemoveDesignerCompilationTask : Task
    {
        [Required]
        public string MigrationFilesPath { get; set; }

        [Required]        
        public string DBContextNamespace { get; set; }

        public int DesignerFileCountToKeep { get; set; } = 2;

        public string IgnoreFilename { get; set; }

        [Output]
        public string[] LatestDesignerFiles { get; set; }

        [Output]
        public string[] ExcludeDesignerFiles { get; set; }

        public override bool Execute()
        {
            var efCoreFileManipulator = new EFCoreFileManipulator(Log);
            var directory = new DirectoryInfo(MigrationFilesPath);

            var (excludeFiles, keptFiles) = efCoreFileManipulator.ProcessDirectory(directory, DBContextNamespace, DesignerFileCountToKeep, IgnoreFilename);

            foreach (var file in excludeFiles)
            {
                Log.LogMessage(MessageImportance.High, $"EF Core migration excluding file {file}");
            }

            
            foreach (var file in keptFiles)
            {
                Log.LogMessage(MessageImportance.High, $"EF Core migration kept file {file}");
            }

            ExcludeDesignerFiles = excludeFiles.OrderBy(x => x).ToArray();

            LatestDesignerFiles = keptFiles.OrderBy(x => x).ToArray();

            Log.LogMessage(MessageImportance.High, $"RDC: Done");
            return !Log.HasLoggedErrors;
        }
    }
}
