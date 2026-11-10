import { isStudentId } from "./domain.js";
export const IDENTITY_KEY = "school-admission.student-id";
type Reader = Pick<Storage, "getItem">;
type Writer = Pick<Storage, "setItem" | "removeItem">;
export function readIdentity(storage?: Reader): string | null {
  try {
    const value = (storage ?? localStorage).getItem(IDENTITY_KEY);
    return value && isStudentId(value) ? value : null;
  } catch {
    return null;
  }
}
export function saveIdentity(value: string | null, storage?: Writer): boolean {
  try {
    const target = storage ?? localStorage;
    if (value === null) target.removeItem(IDENTITY_KEY);
    else {
      if (!isStudentId(value)) return false;
      target.setItem(IDENTITY_KEY, value);
    }
    return true;
  } catch {
    return false;
  }
}
