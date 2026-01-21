using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Domain.interfaces
{
    public interface IValorRepository
    {
        Task<IEnumerable<Valor>> GetAllAsync();
    }
}
