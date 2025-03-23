// Models/SubjectMetadata.cs
using System;

namespace CameraBurstApp.Models
{
    public class SubjectMetadata
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public int TakeNumber { get; set; }
        public DateTime Timestamp { get; set; }

        public SubjectMetadata()
        {
            Timestamp = DateTime.Now;
        }
    }
}