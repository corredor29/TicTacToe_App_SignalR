using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Application.Interfaces;
using Domain.Dtos;
using Domain.Entities;
using Infrastructure.Helpers;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly TicTacToeDbContext _dbContext;
    private readonly IUserConnectionService _userConnectionService;
    private readonly IConfiguration _configuration;

    public UserController(TicTacToeDbContext dbContext, IUserConnectionService userConnectionService, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _userConnectionService = userConnectionService;
        _configuration = configuration;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] User userObj)
    {
        if (userObj == null) return BadRequest();

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == userObj.Username);
        if (user == null) return NotFound(new { message = "User Not Found!!" });

        if (!PasswordHasher.VerifyPassword(userObj.Password!, user.Password!))
            return NotFound(new { message = "Password is Incorrect!" });

        _userConnectionService.AddUserToList(user.Username!);

        user.Token = CreateJwt(user);
        var newAccessToken = user.Token;
        var newRefreshToken = CreateRefreshToken();
        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = DateTime.Now.AddDays(5);

        await _dbContext.SaveChangesAsync();

        return Ok(new TokenDto { AccessToken = newAccessToken, RefreshToken = newRefreshToken });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] User userObj)
    {
        if (userObj == null) return BadRequest();
        if (string.IsNullOrEmpty(userObj.Username) || string.IsNullOrEmpty(userObj.Password))
            return BadRequest(new { message = "Username and password cannot be empty!" });

        var exists = await _dbContext.Users.AnyAsync(u => u.Username == userObj.Username);
        if (exists) return BadRequest(new { message = "Username already exists!!" });

        var passwordMsg = CheckPasswordStrength(userObj.Password);
        if (!string.IsNullOrEmpty(passwordMsg)) return BadRequest(new { message = passwordMsg });

        userObj.Password = PasswordHasher.HashPassword(userObj.Password);

        await _dbContext.Users.AddAsync(userObj);
        await _dbContext.SaveChangesAsync();
        return Ok(new { message = "Register Success!!" });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] TokenDto tokenDto)
    {
        if (tokenDto == null ||
            string.IsNullOrWhiteSpace(tokenDto.AccessToken) ||
            string.IsNullOrWhiteSpace(tokenDto.RefreshToken))
        {
            return BadRequest(new { message = "Access token and refresh token are required." });
        }

        ClaimsPrincipal principal;
        try
        {
            principal = GetPrincipleFromExpiredToken(tokenDto.AccessToken);
        }
        catch (SecurityTokenException)
        {
            return BadRequest(new { message = "Invalid access token." });
        }
        catch (ArgumentException)
        {
            return BadRequest(new { message = "Invalid access token." });
        }

        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest(new { message = "Invalid access token." });

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null || user.RefreshToken != tokenDto.RefreshToken || user.RefreshTokenExpiryTime <= DateTime.Now)
            return BadRequest(new { message = "Invalid refresh request." });

        var newAccessToken = CreateJwt(user);
        var newRefreshToken = CreateRefreshToken();
        user.Token = newAccessToken;
        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = DateTime.Now.AddDays(5);

        await _dbContext.SaveChangesAsync();

        return Ok(new TokenDto { AccessToken = newAccessToken, RefreshToken = newRefreshToken });
    }

    private ClaimsPrincipal GetPrincipleFromExpiredToken(string token)
    {
        var key = _configuration["Jwt:Key"]!;
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)),
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principle = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken securityToken);

        var jwtSecurityToken = securityToken as JwtSecurityToken;
        if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCulture))
            throw new SecurityTokenException("Invalid Token");

        return principle;
    }

    private string CheckPasswordStrength(string password)
    {
        var sb = new StringBuilder();
        if (password.Length < 8) sb.AppendLine("Minimum password length should be 8");
        if (!(Regex.IsMatch(password, "[a-z]") && Regex.IsMatch(password, "[A-Z]") && Regex.IsMatch(password, "[0-9]")))
            sb.AppendLine("Password should be alphanumeric");
        if (!Regex.IsMatch(password, "[<,>,!,#,%,~,_,+,=,@,*]"))
            sb.AppendLine("Password should contain special chars: <,>,!,#,%,~,_,+,=,@,*");
        return sb.ToString();
    }

    private string CreateJwt(User user)
    {
        var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]!);
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, user.Username!) });
        var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = identity,
            Expires = DateTime.Now.AddDays(1),
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private string CreateRefreshToken()
    {
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var tokenInUse = _dbContext.Users.Any(u => u.RefreshToken == refreshToken);
        return tokenInUse ? CreateRefreshToken() : refreshToken;
    }
}
