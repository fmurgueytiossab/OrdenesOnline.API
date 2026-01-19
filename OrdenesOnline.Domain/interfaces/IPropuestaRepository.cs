using OrdenesOnline.Domain.entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Domain.interfaces
{
    public interface IPropuestaRepository
    {
        Task<IEnumerable<Propuesta>> GetAllAsync();
        Task<Propuesta?> GetByIdAsync(int id);
        Task AddAsync(Propuesta propuesta);
        Task UpdateAsync(Propuesta propuesta);
        Task DeleteAsync(int id);

    }
}
