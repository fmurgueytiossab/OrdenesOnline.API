using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrdenesOnline.Application.Services
{
    public class RepresentanteService
    {
        private readonly IRepresentanteRepository _repo;

        public RepresentanteService(IRepresentanteRepository repo)
        {
            _repo = repo;
        }

        public Task<IEnumerable<Representante>> GetAll() => _repo.GetAllAsync();
        public Task<Representante?> GetById(int id) => _repo.GetByIdAsync(id);
        public Task Add(Representante c) => _repo.AddAsync(c);
        public Task Update(Representante c) => _repo.UpdateAsync(c);
        public Task Delete(int id) => _repo.DeleteAsync(id);
        public Task<PasswordValidationResult?> ValidatePassword(string correoCorporativo, string password) => _repo.ValidatePassword(correoCorporativo,password);
    }
}
