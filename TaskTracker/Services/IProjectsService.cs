using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using TaskTracker.Models;

namespace TaskTracker.Services
{
    public interface IProjectsService
    {
        ObservableCollection<ProjectModel> projectModels { get; }

        void AddProject(string project);

        void RemoveProject(string project);

        void AddTaskToProject(string project, TaskModel task);

        void RemoveTaskFromProject(string project, TaskModel task);
    }
}
