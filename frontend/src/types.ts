export type HoldStatus = "HELD" | "CONFIRMED" | "EXPIRED" | "CANCELLED";
export interface ClassOffering {
  id: string;
  name: string;
  totalSeats: number;
  heldSeats: number;
  confirmedSeats: number;
  availableSeats: number;
}
export type CreatedClass = Pick<ClassOffering, "id" | "name" | "totalSeats">;
export interface StudentHold {
  holdId: string;
  classId: string;
  className: string;
  status: HoldStatus;
  expiresAt: string;
}
export interface HoldDetails {
  holdId: string;
  classId: string;
  status: HoldStatus;
  expiresAt: string;
}
export interface CreatedHold extends HoldDetails {
  studentId: string;
}
export interface Confirmation {
  holdId: string;
  status: HoldStatus;
}
