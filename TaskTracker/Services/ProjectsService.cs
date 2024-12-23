using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskTracker.Models;

namespace TaskTracker.Services
{
    public class ProjectsService : IProjectsService
    {
        public ObservableCollection<ProjectModel> projectModels { get; set; }

        public ObservableCollection<ProjectModel> RecentProjects { get; } = new ObservableCollection<ProjectModel>();

        public ProjectsService() 
        {
            projectModels = Config.LoadProjects();
            RecentProjects = Config.LoadRecentProjects();
        }

        public void AddRecentProject(ProjectModel project)
        {
            ProjectModel? existingProjetc = RecentProjects.FirstOrDefault(x => x.Name == project.Name);

            if (existingProjetc != null)
            {
                RecentProjects.Remove(existingProjetc);
            }

            RecentProjects.Insert(0, project);

            if (RecentProjects.Count > 5)
            {
                RecentProjects.RemoveAt(RecentProjects.Count - 1);
            }
        }

        public void AddProject(string project, string description)
        {
            projectModels.Add(new ProjectModel() { Name = project, Description = description });
            Config.SaveProjects(projectModels);
        }

        public void RemoveProject(ProjectModel project)
        {
            projectModels.Remove(project);
            RecentProjects.Remove(project);
        }

        public void ChangeProjectName(ProjectModel project, string newName)
        {
            project.Name = newName;
        }

        public void ChangeProjectDescription(ProjectModel project, string newDescription)
        {
            var projectModel = project;
            projectModel.Description = newDescription;
        }

        public void AddTaskToProject(ProjectModel project, TaskModel task)
        {
            project.Tasks.Add(task);
        }

        public void RemoveTaskFromProject(ProjectModel project, TaskModel task)
        {
            project.Tasks.Remove(task);
        }
    }
}
