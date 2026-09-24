namespace Lidessa.Api.Dtos.QuizAttempts;

public class QuizAttemptStartResponse
{
    public DateTime StartedAt { get; set; }

    // Null si el examen no tiene tiempo límite.
    public int? TimeLimitMinutes { get; set; }

    // Segundos que le quedan según el reloj del servidor (0 si ya se acabó).
    // El frontend arma su cronómetro con esto en vez de con su propio reloj,
    // así recargar la página no reinicia el tiempo.
    public int? RemainingSeconds { get; set; }
}
