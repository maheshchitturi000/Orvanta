export interface UserProfile {
  id: string;
  fullName: string;
  email: string;
  phoneNumber: string;
  role: string;
}

export interface AuthSession {
  accessToken: string;
  expiresAtUtc: string;
  expiresInSeconds: number;
  user: UserProfile;
}

export interface RegisterRequest {
  fullName: string;
  email: string;
  phoneNumber: string;
  password: string;
  confirmPassword: string;
  acceptTerms: boolean;
}

export interface ResetPasswordRequest {
  email: string;
  code: string;
  newPassword: string;
  confirmPassword: string;
}

export interface RegisterResult {
  userId: string;
  email: string;
  otpExpiresInSeconds: number;
  resendAfterSeconds: number;
}

export interface OtpSentResult {
  email: string;
  otpExpiresInSeconds: number;
  resendAfterSeconds: number;
}

export interface VerificationRequired {
  requiresVerification: boolean;
  email: string;
}
