using Microsoft.AspNetCore.Identity;

namespace ApiBe.Models;

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { Id = Guid.CreateVersion7(); }
    public ApplicationRole(string roleName) : this() { Name = roleName; }
}