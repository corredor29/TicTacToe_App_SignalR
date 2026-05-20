using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Domain.Enums;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
    public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
    {
        if (loginDto == null) return BadRequest();
        if (string.IsNullOrWhiteSpace(loginDto.Username) || string.IsNullOrWhiteSpace(loginDto.Password))
            return BadRequest(new { message = "Username and password are required." });

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == loginDto.Username);
        if (user == null) return Unauthorized(new { message = "Invalid credentials." });

        if (!PasswordHasher.VerifyPassword(loginDto.Password, user.Password!))
            return Unauthorized(new { message = "Invalid credentials." });

        _userConnectionService.AddUserToList(user.Username!);
        _userConnectionService.SetUserStatus(user.Username!, UserAvailabilityStatus.Available);

        user.StatusId = (int)UserAvailabilityStatus.Available;
        user.Token = CreateJwt(user);
        var newAccessToken = user.Token;
        var newRefreshToken = CreateRefreshToken();
        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(5);

        await _dbContext.SaveChangesAsync();

        return Ok(new TokenDto { AccessToken = newAccessToken, RefreshToken = newRefreshToken });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
    {
        if (registerDto == null) return BadRequest();
        if (string.IsNullOrWhiteSpace(registerDto.Username) || string.IsNullOrWhiteSpace(registerDto.Password))
            return BadRequest(new { message = "Username and password cannot be empty!" });

        var username = registerDto.Username.Trim();
        var exists = await _dbContext.Users.AnyAsync(u => u.Username == username);
        if (exists) return BadRequest(new { message = "Username already exists!!" });

        var passwordMsg = CheckPasswordStrength(registerDto.Password);
        if (!string.IsNullOrEmpty(passwordMsg)) return BadRequest(new { message = passwordMsg });

        var user = new User
        {
            Username = username,
            Password = PasswordHasher.HashPassword(registerDto.Password),
            StatusId = (int)UserAvailabilityStatus.Available
        };

        await _dbContext.Users.AddAsync(user);
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
        if (user == null || user.RefreshToken != tokenDto.RefreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            return BadRequest(new { message = "Invalid refresh request." });

        var newAccessToken = CreateJwt(user);
        var newRefreshToken = CreateRefreshToken();
        user.Token = newAccessToken;
        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(5);

        await _dbContext.SaveChangesAsync();

        return Ok(new TokenDto { AccessToken = newAccessToken, RefreshToken = newRefreshToken });
    }

    [Authorize]
    [HttpGet("me/status")]
    public IActionResult GetMyStatus()
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized(new { message = "Unauthorized user." });
        }

        var status = _userConnectionService.GetUserStatus(username);
        var result = UserStatusDto.Create(
            username,
            isOnline: true,
            isInPrivateRoom: _userConnectionService.IsUserInPrivateRoom(username),
            status: status);

        return Ok(result);
    }

    [Authorize]
    [HttpPut("me/status")]
    public async Task<IActionResult> UpdateMyStatus([FromBody] UpdateUserStatusDto request)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized(new { message = "Unauthorized user." });
        }

        if (!UserAvailabilityStatusExtensions.IsValid(request.StatusId))
        {
            return BadRequest(new { message = "Invalid status. Use 1=Disponible, 2=Jugando, 3=No molestar." });
        }

        if (_userConnectionService.IsUserInPrivateRoom(username) && request.StatusId != (int)UserAvailabilityStatus.Playing)
        {
            return BadRequest(new { message = "No puedes cambiar a otro estado mientras la partida sigue activa." });
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Username == username);
        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        user.StatusId = request.StatusId;
        _userConnectionService.SetUserStatus(username, (UserAvailabilityStatus)request.StatusId);

        await _dbContext.SaveChangesAsync();

        var result = UserStatusDto.Create(
            username,
            isOnline: true,
            isInPrivateRoom: _userConnectionService.IsUserInPrivateRoom(username),
            status: (UserAvailabilityStatus)request.StatusId);

        return Ok(result);
    }

    [HttpGet("ranking")]
    public async Task<IActionResult> GetRanking()
    {
        var ranking = await _dbContext.Users
            .AsNoTracking()
            .OrderByDescending(x => x.Wins * 3 + x.Draws)
            .ThenByDescending(x => x.Wins)
            .ThenBy(x => x.Losses)
            .ThenBy(x => x.Username)
            .Select(x => new RankingEntryDto
            {
                Username = x.Username ?? string.Empty,
                Wins = x.Wins,
                Losses = x.Losses,
                Draws = x.Draws,
                GamesPlayed = x.GamesPlayed,
                Score = (x.Wins * 3) + x.Draws,
                WinRate = x.GamesPlayed == 0
                    ? 0
                    : Math.Round((decimal)x.Wins * 100 / x.GamesPlayed, 2)
            })
            .ToListAsync();

        return Ok(ranking);
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
            Expires = DateTime.UtcNow.AddDays(1),
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
