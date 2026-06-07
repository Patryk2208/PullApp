// (wg trip-planner-spec.md)
export interface GeoPoint {
    lat: number;
    lng: number;
}

export interface PublishTripRequest {
    start: GeoPoint;
    end: GeoPoint;
}

export interface PublishTripFormState {
    startAddress: string; 
    endAddress: string;    
}

export interface RideMatchingQuery {
    start: GeoPoint;
    end: GeoPoint;
    departureDate: string; // ISO 8601
    seatsNeeded: number;
    maxDetourKm: number;
    timeWindowMinutes: number;
}