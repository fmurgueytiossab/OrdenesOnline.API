using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Application.Services
{
    public class ValorService
    {
        private readonly IValorRepository _repo;

        public ValorService(IValorRepository repo)
        {
            _repo = repo;
        }

        public Task<IEnumerable<Valor>> GetAll() => _repo.GetAllAsync();
    }
}
