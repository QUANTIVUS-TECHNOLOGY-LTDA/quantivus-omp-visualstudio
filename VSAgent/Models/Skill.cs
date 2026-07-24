using System;

namespace VSAgent.Models
{
    public sealed class Skill
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
    }
}
