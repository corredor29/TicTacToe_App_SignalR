using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Domain.Dtos;
using Domain.Entities;
using Google.Apis.Auth;
using Infrastructure.Helpers;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Api.Controllers;

[Route("api/auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly TicTacToeDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public AuthController(TicTacToeDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleTokenDto dto)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _configuration["Google:ClientId"] }
            };
            var payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, settings);

            var username = payload.Email.Split('@')[0].Replace(".", "_");

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
            {
                user = new User
                {
                    Username = username,
                    Password = PasswordHasher.HashPassword(Guid.NewGuid().ToString())
                };
                await _dbContext.Users.AddAsync(user);
                await _dbContext.SaveChangesAsync();
            }

            var accessToken  = CreateJwt(user);
            var refreshToken = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));

            user.Token = accessToken;
            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(5);
            await _dbContext.SaveChangesAsync();

            return Ok(new TokenDto { AccessToken = accessToken, RefreshToken = refreshToken });
        }
        catch (Exception)
        {
            return Unauthorized(new { message = "Invalid Google token." });
        }
    }

    private string CreateJwt(User user)
    {
        var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]!);
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, user.Username!) });
        var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = identity,
            Expires = DateTime.UtcNow.AddDays(1),
            SigningCredentials = credentials
        };
        var tokenHandler = new JwtSecurityTokenHandler();
        return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
    }
}