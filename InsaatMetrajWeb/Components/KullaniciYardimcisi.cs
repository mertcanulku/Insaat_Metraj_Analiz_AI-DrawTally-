using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace InsaatMetrajWeb.Components;

public static class KullaniciYardimcisi
{
    /// <summary>Oturum açmış kullanıcının Identity kimliğini (AspNetUsers.Id) döner.</summary>
    public static async Task<string> GetUserIdAsync(this AuthenticationStateProvider provider)
    {
        var state = await provider.GetAuthenticationStateAsync();
        return state.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Kullanıcı kimliği bulunamadı.");
    }
}
