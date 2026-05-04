namespace Utility.DomainService.Sequence.service
{
    public interface ISequenceRepository
    {
        Task<long> GetNextSequenceNumberAsync(string context);
        Task<long> GetNextHexSequenceNumberAsync(string context);
        Task ResetSequenceNumberAsync(string context, long startNumber);
    }
}