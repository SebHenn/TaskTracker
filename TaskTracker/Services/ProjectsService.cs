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

        public ProjectsService() 
        {
            projectModels = new ObservableCollection<ProjectModel>();
        }

        public void AddProject(string project)
        {
            projectModels.Add(new ProjectModel() { Name = project });
        }

        public void RemoveProject(string project)
        {
            projectModels.Remove(projectModels.FirstOrDefault(x => x.Name == project));
        }

        public void AddTaskToProject(string project, TaskModel task)
        {
            projectModels.FirstOrDefault(x => x.Name == project).Tasks.Add(task);
        }

        public void RemoveTaskFromProject(string project, TaskModel task)
        {
            projectModels.FirstOrDefault(x => x.Name == project).Tasks.Remove(task);
        }
    }
}
