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
            var datos = await _context.Valores
                        .Where(v => v.Estado != "9")
                        .Select(v => new Valor
                        {
                            Cosabval = v.Cosabval,
                            Mnemo = v.Mnemo,
                            Comon = v.Comon
                        })
                        .ToListAsync();

            return datos;

        }
    }
}
