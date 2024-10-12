using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskTracker.Models
{
    public class TaskModel
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public bool IsDone {  get; set; } 
    }
}
