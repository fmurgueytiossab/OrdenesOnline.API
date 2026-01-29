using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Application.Services
{
    public class PropuestaService
    {
        private readonly IPropuestaRepository _repo;

        public PropuestaService(IPropuestaRepository repo)
        {
            _repo = repo;
        }

        public Task<IEnumerable<Propuesta>> GetAll() => _repo.GetAllAsync();
        public Task<Propuesta?> GetById(int id) => _repo.GetByIdAsync(id);
        public Task Add(Propuesta c) => _repo.AddAsync(c);
        public Task Update(Propuesta c) => _repo.UpdateAsync(c);
        public Task Delete(int id) => _repo.DeleteAsync(id);
    }
}
