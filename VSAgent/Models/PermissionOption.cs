namespace VSAgent.Models
{
    public sealed class PermissionOption
    {
        public string OptionId { get; }
        public string Name { get; }
        public string Kind { get; }

        public PermissionOption(string optionId, string name, string kind)
        {
            OptionId = optionId;
            Name = name;
            Kind = kind;
        }
    }
}
