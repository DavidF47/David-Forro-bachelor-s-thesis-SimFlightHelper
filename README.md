# David-Forró-bachelor-s-thesis
# Pilot Support Application for Flight Simulation

This repository contains the practical implementation of my bachelor thesis project:

**Development of an Application to Support Flight Simulator Pilots Using Real-Time Air Traffic Data**

The aim of the project is to create a desktop application that supports flight simulator pilots during pre-flight preparation. The application combines several types of aviation-related information in one interface, including meteorological reports, airport charts, and route generation.

## Overview

In flight simulation, users often rely on multiple separate tools for weather information, airport procedures, charts, and flight planning. This can make the preparation process fragmented, especially for users who do not have deeper aviation knowledge.

This application addresses that issue by providing a simplified integrated environment where the user can:

- retrieve and interpret METAR reports,
- retrieve and interpret TAF forecasts,
- view airport charts,
- generate a basic flight route for simulation purposes,
- display the generated route on an interactive map.

The project is intended for educational and simulation use only. It is not suitable for real-world flight planning or operational aviation.

## Main Features

### METAR Processing

The application can retrieve METAR reports for selected airports and display the decoded information in a structured and readable form. It handles different wind, visibility, cloud, and weather formats that may appear in raw METAR text.

### TAF Processing

TAF forecasts are retrieved and divided into forecast periods. The application identifies forecast change types such as:

- `BASE`
- `TEMPO`
- `BECMG`
- `FM`
- `PROB30`
- `PROB40`

The goal is to make forecast periods easier to understand and compare.

### Airport Chart Display

The application supports airport chart viewing from two sources:

- locally stored PDF files,
- online chart data through AviationAPI.

Local charts are loaded from folders based on ICAO airport codes. If local charts are not available, the application attempts to retrieve charts from the online API. The online chart source is mainly useful for airports in the United States.

### Route Generation

The route planner generates a usable route between two airports in a simulation environment. The route consists of:

- departure airport,
- selected SID procedure,
- enroute section,
- selected STAR procedure,
- destination airport.

The enroute section is calculated using a graph-based approach based on navigation points and airways.

### Map Visualization

Generated routes are displayed on an interactive Leaflet map inside the Windows Forms application using WebView2. The route is shown as a polyline, with markers for the origin and destination airports.

## Technologies Used

- C#
- Windows Forms
- WebView2
- JavaScript
- Leaflet
- OpenStreetMap
- JSON processing
- REST API communication

## External Data Sources

The project uses several external data sources and APIs:

- CheckWX API for METAR and TAF data
- AeroDataBox API for airport lookup
- AviationAPI for airport charts
- SimpleMaps worldcities dataset for city autocomplete
- X-Plane 11 navigation data files for route generation

The route planner uses files such as:

- `earth_fix.dat`
- `earth_nav.dat`
- `earth_awy.dat`
- CIFP procedure files

## Limitations

This application has several important limitations:

- It is designed for flight simulation only.
- It is not suitable for real-world aviation use.
- Navigation and procedure data may be incomplete or outdated.
- SID and STAR processing is only partially reliable due to data limitations.
- Online chart coverage is mainly limited to United States airports.
- NOTAM and ASHTAM processing are not implemented.
- The route generator does not perform full real-world flight plan validation.

## Project Structure

The application is organized into several modules:

```text
/Charts
    Local airport chart PDFs organized by ICAO code

/Data
    Navigation and airport data files

/Planner
    Route generation and graph-based pathfinding logic

/Weather
    METAR and TAF retrieval and processing

/Map
    Leaflet map display and route visualization

/Forms
    Windows Forms user interface
