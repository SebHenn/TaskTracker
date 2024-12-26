using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskTracker.Models;

namespace TaskTracker.Messages
{
    public class ProjectSelectClickMessage : ValueChangedMessage<ProjectModel>
    {
        public ProjectSelectClickMessage(ProjectModel value) : base(value)
        {
        }
    }
}
