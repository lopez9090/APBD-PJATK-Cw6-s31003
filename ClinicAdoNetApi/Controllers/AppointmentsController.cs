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

        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = string.IsNullOrEmpty(status) ? DBNull.Value : status;
        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 80).Value = string.IsNullOrEmpty(patientLastName) ? DBNull.Value : patientLastName;

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
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes")) ? null : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber"))
        };

        return Ok(details);
    }
}