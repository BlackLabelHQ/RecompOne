namespace RecompOne.Runtime.Bios;

//just for the bios logs, better than doing one print by case, possibely missed somes but who cares anyways bruh
public static class BiosNames
{
    public static string A(uint fn)
    {
        return fn switch
        {
            0x00 => "open", 0x01 => "lseek", 0x02 => "read", 0x03 => "write", 0x04 => "close",
            0x05 => "ioctl", 0x06 => "exit", 0x07 => "isatty", 0x08 => "getc", 0x09 => "putc",
            0x0A => "todigit", 0x0B => "atof", 0x0C => "strtoul", 0x0D => "strtol", 0x0E => "abs",
            0x0F => "labs", 0x10 => "atoi", 0x11 => "atol", 0x12 => "atob", 0x13 => "setjmp",
            0x14 => "longjmp", 0x15 => "strcat", 0x16 => "strncat", 0x17 => "strcmp", 0x18 => "strncmp",
            0x19 => "strcpy", 0x1A => "strncpy", 0x1B => "strlen", 0x1C => "index", 0x1D => "rindex",
            0x1E => "strchr", 0x1F => "strrchr", 0x20 => "strpbrk", 0x21 => "strspn", 0x22 => "strcspn",
            0x23 => "strtok", 0x24 => "strstr", 0x25 => "toupper", 0x26 => "tolower", 0x27 => "bcopy",
            0x28 => "bzero", 0x29 => "bcmp", 0x2A => "memcpy", 0x2B => "memset", 0x2C => "memmove",
            0x2D => "memcmp", 0x2E => "memchr", 0x2F => "rand", 0x30 => "srand", 0x31 => "qsort",
            0x32 => "strtod", 0x33 => "malloc", 0x34 => "free", 0x35 => "lsearch", 0x36 => "bsearch",
            0x37 => "calloc", 0x38 => "realloc", 0x39 => "InitHeap", 0x3A => "_exit", 0x3B => "getchar",
            0x3C => "putchar", 0x3D => "gets", 0x3E => "puts", 0x3F => "printf",
            0x40 => "SystemErrorUnresolvedException", 0x41 => "LoadTest", 0x42 => "Load", 0x43 => "Exec",
            0x44 => "FlushCache", 0x45 => "init_a0_b0_c0_vectors", 0x46 => "GPU_dw", 0x47 => "gpu_send_dma",
            0x48 => "SendGP1Command", 0x49 => "GPU_cw", 0x4A => "GPU_cwp", 0x4B => "send_gpu_linked_list",
            0x4C => "gpu_abort_dma", 0x4D => "GetGPUStatus", 0x4E => "gpu_sync", 0x51 => "LoadExec",
            0x54 => "_96_init", 0x55 => "_bu_init", 0x56 => "_96_remove", 0x70 => "_bu_init",
            0x71 => "_96_init", 0x72 => "_96_remove", 0x78 => "CdAsyncSeekL", 0x7C => "CdAsyncGetStatus",
            0x7E => "CdAsyncReadSector", 0x81 => "CdAsyncSetMode", 0x9C => "SetConf", 0x9D => "GetConf",
            0xA0 => "_boot", 0xA1 => "SystemError", 0xA2 => "EnqueueCdIntr", 0xA3 => "DequeueCdIntr",
            0xA4 => "CdGetLbn", 0xA5 => "CdReadSector", 0xA6 => "CdGetStatus", 0xB4 => "GetSystemInfo",
            _ => "?"
        };
    }

    public static string B(uint fn)
    {
        return fn switch
        {
            0x00 => "alloc_kernel_memory", 0x01 => "free_kernel_memory", 0x02 => "init_timer",
            0x03 => "get_timer", 0x04 => "enable_timer_irq", 0x05 => "disable_timer_irq",
            0x06 => "restart_timer", 0x07 => "DeliverEvent", 0x08 => "OpenEvent", 0x09 => "CloseEvent",
            0x0A => "WaitEvent", 0x0B => "TestEvent", 0x0C => "EnableEvent", 0x0D => "DisableEvent",
            0x0E => "OpenTh", 0x0F => "CloseTh", 0x10 => "ChangeTh", 0x12 => "InitPAD2", 0x13 => "StartPAD2",
            0x14 => "StopPAD2", 0x15 => "PAD_init2", 0x16 => "PAD_dr", 0x17 => "ReturnFromException",
            0x18 => "ResetEntryInt", 0x19 => "HookEntryInt", 0x20 => "UnDeliverEvent", 0x32 => "open",
            0x33 => "lseek", 0x34 => "read", 0x35 => "write", 0x36 => "close", 0x37 => "ioctl",
            0x38 => "exit", 0x39 => "isatty", 0x3A => "getc", 0x3B => "putc", 0x3C => "getchar",
            0x3D => "putchar", 0x3E => "gets", 0x3F => "puts", 0x40 => "cd", 0x41 => "format",
            0x42 => "firstfile2", 0x43 => "nextfile", 0x44 => "rename", 0x45 => "erase", 0x46 => "undelete",
            0x49 => "PrintInstalledDevices", 0x4A => "InitCARD2", 0x4B => "StartCARD2", 0x4C => "StopCARD2",
            0x4D => "_card_info_subfunc", 0x4E => "_card_write", 0x4F => "_card_read", 0x50 => "_new_card",
            0x51 => "Krom2RawAdd", 0x53 => "Krom2Offset", 0x54 => "_get_errno", 0x55 => "_get_error",
            0x56 => "GetC0Table", 0x57 => "GetB0Table", 0x58 => "_card_chan", 0x59 => "testdevice",
            0x5C => "_card_status", 0x5D => "_card_wait",
            _ => "?"
        };
    }

    public static string C(uint fn)
    {
        return fn switch
        {
            0x00 => "EnqueueTimerAndVblankIrqs", 0x01 => "EnqueueSyscallHandler", 0x02 => "SysEnqIntRP",
            0x03 => "SysDeqIntRP", 0x04 => "get_free_EvCB_slot", 0x05 => "get_free_TCB_slot",
            0x06 => "ExceptionHandler", 0x07 => "InstallExceptionHandlers", 0x08 => "SysInitMemory",
            0x09 => "SysInitKernelVariables", 0x0A => "ChangeClearRCnt", 0x0C => "InitDefInt",
            0x0D => "SetIrqAutoAck", 0x12 => "InstallDevices", 0x13 => "FlushStdInOutPut",
            0x19 => "_ioabort", 0x1A => "set_card_find_mode", 0x1B => "KernelRedirect",
            0x1C => "AdjustA0Table", 0x1D => "get_card_find_mode",
            _ => "?"
        };
    }
}