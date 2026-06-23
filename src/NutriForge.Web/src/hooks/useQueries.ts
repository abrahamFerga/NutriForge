import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { diaryApi, profileApi, targetsApi } from "@/lib/api";
import { queryKeys } from "@/lib/queryKeys";
import type {
  CreateDiaryEntryRequest,
  UpdateProfileRequest,
} from "@/lib/types";

export function useProfile() {
  return useQuery({
    queryKey: queryKeys.profile,
    queryFn: () => profileApi.get(),
  });
}

export function useTargets() {
  return useQuery({
    queryKey: queryKeys.targets,
    queryFn: () => targetsApi.get(),
  });
}

export function useDiary(date: string) {
  return useQuery({
    queryKey: queryKeys.diary(date),
    queryFn: () => diaryApi.get(date),
  });
}

export function useTrend(days = 7) {
  return useQuery({
    queryKey: queryKeys.trend(days),
    queryFn: () => diaryApi.trend(days),
  });
}

export function useSaveProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateProfileRequest) => profileApi.update(body),
    onSuccess: (saved) => {
      qc.setQueryData(queryKeys.profile, saved);
      // Targets depend on profile; diary embeds targets.
      qc.invalidateQueries({ queryKey: queryKeys.targets });
      qc.invalidateQueries({ queryKey: queryKeys.diaryAll });
    },
  });
}

export function useAddDiaryEntry() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateDiaryEntryRequest) => diaryApi.create(body),
    onSuccess: (entry) => {
      qc.invalidateQueries({ queryKey: queryKeys.diary(entry.date) });
      qc.invalidateQueries({ queryKey: queryKeys.diaryAll });
    },
  });
}

export function useDeleteDiaryEntry(date: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => diaryApi.remove(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.diary(date) });
      qc.invalidateQueries({ queryKey: queryKeys.diaryAll });
    },
  });
}

/** Debounce a changing value by `delay` ms. */
import { useEffect, useState } from "react";
export function useDebounced<T>(value: T, delay = 350): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(id);
  }, [value, delay]);
  return debounced;
}
