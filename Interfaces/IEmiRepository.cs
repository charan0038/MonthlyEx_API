using MonthExpenses.Api.DTOs;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Interfaces;

public interface IEmiRepository
{
    Task<IEnumerable<EmiDto>> GetEmisAsync(int userId);
    Task<EmiDto?> GetEmiByIdAsync(int id, int userId);
    Task<EmiLoan> CreateEmiAsync(EmiLoan emi);
    Task<EmiLoan?> UpdateEmiAsync(int id, UpdateEmiDto dto, int userId);
    Task<bool> DeleteEmiAsync(int id, int userId);
    Task<List<EmiPaymentItemDto>> GetEmiPaymentHistoryAsync(int categoryId, int userId);
}
