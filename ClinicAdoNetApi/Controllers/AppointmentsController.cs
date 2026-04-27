using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using ClinicAdoNetApi.DTO;


namespace ClinicAdoNetApi.Controllers;

[ApiController]
[Route("api/[controller]")]

public class AppointmentsController : ControllerBase
{
    private readonly string _connectionString;

    public AppointmentsController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
                            ?? throw new InvalidOperationException("No connection string found");
    }

    [HttpGet]
    public async Task<IActionResult> GetAppointments([FromQuery] string? status, [FromQuery] string? patientLastName)
    {
        var appointments = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);

        string sql = @"
            SELECT 
                a.IdAppointment, 
                a.AppointmentDate, 
                a.Status, 
                a.Reason, 
                CONCAT(p.FirstName, ' ', p.LastName) AS PatientFullName, 
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
        ";

        await using var command = new SqlCommand(sql, connection);

        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrEmpty(status) ? DBNull.Value : status;
        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 80).Value =
            string.IsNullOrEmpty(patientLastName) ? DBNull.Value : patientLastName;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return Ok(appointments);
    }


    [HttpGet("{idAppointment}")]
    public async Task<IActionResult> GetAppointmentDetails(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(@"
            SELECT 
                a.IdAppointment, a.AppointmentDate, a.Status, a.Reason, a.InternalNotes, a.CreatedAt,
                p.FirstName + N' ' + p.LastName AS PatientFullName, p.Email AS PatientEmail, p.PhoneNumber AS PatientPhoneNumber,
                d.LicenseNumber AS DoctorLicenseNumber
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
            WHERE a.IdAppointment = @IdAppointment;
            ", connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return NotFound(new ErrorResponseDto { Message = $"Wizyta o ID {idAppointment} nie istnieje." });
        }

        var details = new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes"))
                ? null
                : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber"))
        };

        return Ok(details);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
    {
        if (request.AppointmentDate <= DateTime.Now)
        {
            return BadRequest(new ErrorResponseDto { Message = "Termin wizyty nie może być w przeszłości." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
        {
            return BadRequest(new ErrorResponseDto
                { Message = "Opis wizyty nie może być pusty i musi mieć maksymalnie 250 znaków." });
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var checkPatientCmd =
            new SqlCommand("SELECT COUNT(1) FROM dbo.Patients WHERE IdPatient = @IdPatient AND IsActive = 1;",
                connection);
        checkPatientCmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
        var patientExists = (int)await checkPatientCmd.ExecuteScalarAsync() > 0;

        if (!patientExists)
        {
            return NotFound(new ErrorResponseDto { Message = "Pacjent nie istnieje lub jest nieaktywny." });
        }

        await using var checkDoctorCmd =
            new SqlCommand("SELECT COUNT(1) FROM dbo.Doctors WHERE IdDoctor = @IdDoctor AND IsActive = 1;", connection);
        checkDoctorCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        var doctorExists = (int)await checkDoctorCmd.ExecuteScalarAsync() > 0;

        if (!doctorExists)
        {
            return NotFound(new ErrorResponseDto { Message = "Lekarz nie istnieje lub jest nieaktywny." });
        }

        await using var checkConflictCmd =
            new SqlCommand(
                "SELECT COUNT(1) FROM dbo.Appointments WHERE IdDoctor = @IdDoctor AND AppointmentDate = @AppointmentDate;",
                connection);
        checkConflictCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        checkConflictCmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        var hasConflict = (int)await checkConflictCmd.ExecuteScalarAsync() > 0;

        if (hasConflict)
        {
            return Conflict(new ErrorResponseDto { Message = "Lekarz ma już zaplanowaną wizytę w tym terminie." });
        }

        await using var insertCmd = new SqlCommand(@"
            INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
            VALUES (@IdPatient, @IdDoctor, @AppointmentDate, N'Scheduled', @Reason);
        ", connection);

        insertCmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
        insertCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        insertCmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        insertCmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;

        await insertCmd.ExecuteNonQueryAsync();

        return StatusCode(201);
    }

    [HttpPut("{idAppointment}")]
    public async Task<IActionResult> UpdateAppointment(int idAppointment,
        [FromBody] UpdateAppointmentRequestDto request)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var checkExistCmd =
            new SqlCommand("SELECT IdDoctor FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;", connection);
        checkExistCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        var existingDoctorId = await checkExistCmd.ExecuteScalarAsync();

        if (existingDoctorId == null)
        {
            return NotFound(new ErrorResponseDto { Message = "Wizyta o ID {idAppointment} nie istnieje." });
        }

        await using var checkConflictCmd = new SqlCommand(@"
            SELECT COUNT(1) FROM dbo.Appointments 
            WHERE IdDoctor = @IdDoctor 
              AND AppointmentDate = @AppointmentDate 
              AND IdAppointment <> @IdAppointment;", connection);

        checkConflictCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = (int)existingDoctorId;
        checkConflictCmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        checkConflictCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        if ((int)await checkConflictCmd.ExecuteScalarAsync() > 0)
        {
            return Conflict(new ErrorResponseDto { Message = "Lekarz ma już inną wizytę w tym terminie." });
        }

        await using var updateCmd = new SqlCommand(@"
            UPDATE dbo.Appointments 
            SET AppointmentDate = @AppointmentDate, 
                Status = @Status, 
                Reason = @Reason, 
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;", connection);

        updateCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        updateCmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        updateCmd.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = request.Status;
        updateCmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;
        updateCmd.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, 500).Value =
            (object?)request.InternalNotes ?? DBNull.Value;

        await updateCmd.ExecuteNonQueryAsync();

        return NoContent();
    }

    [HttpDelete("{idAppointment}")]
    public async Task<IActionResult> DeleteAppointment(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var checkCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;", connection);
        checkCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        if ((int)await checkCmd.ExecuteScalarAsync() == 0)
        {
            return NotFound(new ErrorResponseDto { Message = "Nie znaleziono wizyty do usunięcia." });
        }

        await using var deleteCmd = new SqlCommand("DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;", connection);
        deleteCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        await deleteCmd.ExecuteNonQueryAsync();
        return NoContent();
    }
}