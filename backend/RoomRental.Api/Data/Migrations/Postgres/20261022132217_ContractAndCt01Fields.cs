using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class ContractAndCt01Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CitizenId",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CitizenIdIssuedDate",
                table: "Users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CitizenIdIssuedPlace",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "Users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PermanentAddress",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdditionalTerms",
                table: "Occupancies",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoResidentsJson",
                table: "Occupancies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ContractEndDate",
                table: "Occupancies",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandoverAssets",
                table: "Occupancies",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HouseholdHeadRelation",
                table: "Occupancies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentDueDay",
                table: "Occupancies",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<decimal>(
                name: "RoomAreaSqm",
                table: "Occupancies",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantCitizenId",
                table: "Occupancies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TenantCitizenIdIssuedDate",
                table: "Occupancies",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantCitizenIdIssuedPlace",
                table: "Occupancies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TenantDateOfBirth",
                table: "Occupancies",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantEmail",
                table: "Occupancies",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantGender",
                table: "Occupancies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantPermanentAddress",
                table: "Occupancies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PoliceStationName",
                table: "Buildings",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CitizenId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CitizenIdIssuedDate",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CitizenIdIssuedPlace",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PermanentAddress",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AdditionalTerms",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "CoResidentsJson",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "ContractEndDate",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "HandoverAssets",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "HouseholdHeadRelation",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "PaymentDueDay",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "RoomAreaSqm",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "TenantCitizenId",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "TenantCitizenIdIssuedDate",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "TenantCitizenIdIssuedPlace",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "TenantDateOfBirth",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "TenantEmail",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "TenantGender",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "TenantPermanentAddress",
                table: "Occupancies");

            migrationBuilder.DropColumn(
                name: "PoliceStationName",
                table: "Buildings");
        }
    }
}
