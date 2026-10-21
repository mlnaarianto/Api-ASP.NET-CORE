[ApiController]
[Route("api/auth")]
public class AuthController(UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        if (await userManager.FindByNameAsync(req.UserName) is not null)
            return Conflict(new ProblemDetails
            {
                Title = "Username sudah dipakai",
                Status = StatusCodes.Status409Conflict
            });

        if (await userManager.FindByEmailAsync(req.Email) is not null)
            return Conflict(new ProblemDetails
            {
                Title = "Email sudah terdaftar",
                Status = StatusCodes.Status409Conflict
            });

        var user = new ApplicationUser
        {
            UserName = req.UserName,
            Email = req.Email,
            FullName = req.FullName
        };

        var result = await userManager.CreateAsync(user, req.Password);

        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(e.Code, e.Description);

            return ValidationProblem(ModelState); // 400 dengan format standar
        }

        var response = new UserResponse(user.Id, user.UserName!, user.Email!, user.FullName);
        return Created($"/api/users/{user.Id}", response);
    }
}

public record RegisterRequest(string UserName, string Email, string FullName, string Password);
public record UserResponse(Guid Id, string UserName, string Email, string? FullName);