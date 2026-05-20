namespace Domain.Dtos;

public class MessageDto
{
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Content { get; set; }
}

public class PrivateMessageDto
{
    public string? From { get; set; }
    public string? To { get; set; }
    public int Position { get; set; }
}

public class RematchDecisionDto
{
    public string? From { get; set; }
    public string? To { get; set; }
    public bool Accepted { get; set; }
}
