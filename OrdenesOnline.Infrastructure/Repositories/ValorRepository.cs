using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Infrastructure.Repositories
{
    public class ValorRepository : IValorRepository
    {
        private readonly OpersabDbContext _context;

        public ValorRepository(OpersabDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Valor>> GetAllAsync()
        {
            var datos = _context.Valores
                .Select(v => new Valor
                {
                    Cosabval = v.Cosabval,
                    Desval = v.Desval,
                    Comon = v.Comon
                })
                .ToList();

            return datos;
        }
    }
}
