using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TaskTracker.Models;

namespace TaskTracker
{
    public class Config
    {
        public static string SaveFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TaskTracker", "Save.json");
        public static string SaveRecentFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TaskTracker", "SaveRecent.json");

        public static void SaveProjects(ObservableCollection<ProjectModel> projects)
        {
            foreach (var project in projects)
            {
                project.IsSelected = false;
            }

            var folderPath = Path.GetDirectoryName(SaveFilePath);
            if (folderPath != null && !Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(projects, options);
            File.WriteAllText(SaveFilePath, json);
        }

        public static ObservableCollection<ProjectModel> LoadProjects()
        {
            if (!File.Exists(SaveFilePath)) return new ObservableCollection<ProjectModel>();

            var json = File.ReadAllText(SaveFilePath);
            return JsonSerializer.Deserialize<ObservableCollection<ProjectModel>>(json)
                   ?? new ObservableCollection<ProjectModel>();
        }

        public static void SaveRecentProjects(ObservableCollection<ProjectModel> projects)
        {
            foreach (var project in projects)
            {
                project.IsSelected = false;
            }

            var folderPath = Path.GetDirectoryName(SaveRecentFilePath);
            if (folderPath != null && !Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(projects, options);
            File.WriteAllText(SaveRecentFilePath, json);
        }

        public static ObservableCollection<ProjectModel> LoadRecentProjects()
        {
            if (!File.Exists(SaveRecentFilePath)) return new ObservableCollection<ProjectModel>();

            var json = File.ReadAllText(SaveRecentFilePath);
            return JsonSerializer.Deserialize<ObservableCollection<ProjectModel>>(json)
                   ?? new ObservableCollection<ProjectModel>();
        }
    }
}
